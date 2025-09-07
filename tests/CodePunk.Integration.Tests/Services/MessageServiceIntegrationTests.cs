using FluentAssertions;
using CodePunk.Core.Models;
using System.Text.Json;

namespace CodePunk.Integration.Tests.Services;

/// <summary>
/// Integration tests for MessageService using Ports and Adapters pattern.
/// Tests message CRUD operations with complex MessagePart polymorphism.
/// </summary>
public class MessageServiceIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task CreateMessage_WithTextPart_ShouldPersistAndDeserializeCorrectly()
    {
        // Arrange
        var session = await SessionService.CreateAsync("Test Session");
        
        var message = Message.Create(
            session.Id,
            MessageRole.User,
            new List<MessagePart> { new TextPart("Hello, world!") }
        );
        
        // Act - Test through service interface
        await MessageService.CreateAsync(message);
        
        // Assert - Test through service interface
        var messages = await MessageService.GetBySessionAsync(session.Id);
        messages.Should().HaveCount(1);
        
        var retrievedMessage = messages.First();
        retrievedMessage.Role.Should().Be(MessageRole.User);
        retrievedMessage.Parts.Should().HaveCount(1);
        retrievedMessage.Parts[0].Should().BeOfType<TextPart>();
        
        var textPart = (TextPart)retrievedMessage.Parts[0];
        textPart.Content.Should().Be("Hello, world!");
        textPart.Type.Should().Be(MessagePartType.Text);
    }

    [Fact]
    public async Task CreateMessage_WithMultipleParts_ShouldHandlePolymorphicSerialization()
    {
        // Arrange
        var session = await SessionService.CreateAsync("Test Session");
        
        var toolCallArgs = JsonSerializer.SerializeToElement(new { path = "test.cs" });
        var messageParts = new List<MessagePart>
        {
            new TextPart("Let me read your file:"),
            new ToolCallPart("call_123", "read_file", toolCallArgs),
            new ToolResultPart("call_123", "public class Test { }", false),
            new TextPart("Here's your file content.")
        };
        
        var message = Message.Create(session.Id, MessageRole.Assistant, messageParts);
        
        // Act
        await MessageService.CreateAsync(message);
        
        // Assert
        var messages = await MessageService.GetBySessionAsync(session.Id);
        var retrievedMessage = messages.First();
        
        retrievedMessage.Parts.Should().HaveCount(4);
        
        // Verify each part type and content
        retrievedMessage.Parts[0].Should().BeOfType<TextPart>();
        ((TextPart)retrievedMessage.Parts[0]).Content.Should().Be("Let me read your file:");
        
        retrievedMessage.Parts[1].Should().BeOfType<ToolCallPart>();
        var toolCall = (ToolCallPart)retrievedMessage.Parts[1];
        toolCall.Id.Should().Be("call_123");
        toolCall.Name.Should().Be("read_file");
        
        retrievedMessage.Parts[2].Should().BeOfType<ToolResultPart>();
        var toolResult = (ToolResultPart)retrievedMessage.Parts[2];
        toolResult.ToolCallId.Should().Be("call_123");
        toolResult.Content.Should().Be("public class Test { }");
        toolResult.IsError.Should().BeFalse();
        
        retrievedMessage.Parts[3].Should().BeOfType<TextPart>();
        ((TextPart)retrievedMessage.Parts[3]).Content.Should().Be("Here's your file content.");
    }

    [Fact]
    public async Task GetMessagesBySession_ShouldReturnInChronologicalOrder()
    {
        // Arrange
        var session = await SessionService.CreateAsync("Test Session");
        
        var message1 = Message.Create(session.Id, MessageRole.User, 
            new List<MessagePart> { new TextPart("First message") });
        var message2 = Message.Create(session.Id, MessageRole.Assistant,
            new List<MessagePart> { new TextPart("Second message") });
        var message3 = Message.Create(session.Id, MessageRole.User,
            new List<MessagePart> { new TextPart("Third message") });
            
        // Act - Create messages with small delays to ensure different timestamps
        await MessageService.CreateAsync(message1);
        await Task.Delay(10);
        await MessageService.CreateAsync(message2);
        await Task.Delay(10);
        await MessageService.CreateAsync(message3);
        
        // Assert
        var messages = await MessageService.GetBySessionAsync(session.Id);
        messages.Should().HaveCount(3);
        
        // Should be in chronological order (oldest first)
        messages[0].Role.Should().Be(MessageRole.User);
        ((TextPart)messages[0].Parts[0]).Content.Should().Be("First message");
        
        messages[1].Role.Should().Be(MessageRole.Assistant);
        ((TextPart)messages[1].Parts[0]).Content.Should().Be("Second message");
        
        messages[2].Role.Should().Be(MessageRole.User);
        ((TextPart)messages[2].Parts[0]).Content.Should().Be("Third message");
        
        // Verify timestamps
        messages[0].CreatedAt.Should().BeBefore(messages[1].CreatedAt);
        messages[1].CreatedAt.Should().BeBefore(messages[2].CreatedAt);
    }

    [Fact]
    public async Task CreateMessage_WithImagePart_ShouldSerializeCorrectly()
    {
        // Arrange
        var session = await SessionService.CreateAsync("Test Session");
        
        var message = Message.Create(
            session.Id,
            MessageRole.User,
            new List<MessagePart> 
            { 
                new TextPart("Look at this image:"),
                new ImagePart("https://example.com/image.jpg", "A test image")
            }
        );
        
        // Act
        await MessageService.CreateAsync(message);
        
        // Assert
        var messages = await MessageService.GetBySessionAsync(session.Id);
        var retrievedMessage = messages.First();
        
        retrievedMessage.Parts.Should().HaveCount(2);
        retrievedMessage.Parts[0].Should().BeOfType<TextPart>();
        retrievedMessage.Parts[1].Should().BeOfType<ImagePart>();
        
        var imagePart = (ImagePart)retrievedMessage.Parts[1];
        imagePart.Url.Should().Be("https://example.com/image.jpg");
        imagePart.Description.Should().Be("A test image");
        imagePart.Type.Should().Be(MessagePartType.Image);
    }

    [Fact]
    public async Task DeleteMessage_ShouldRemoveMessageThroughService()
    {
        // Arrange
        var session = await SessionService.CreateAsync("Test Session");
        
        var message = Message.Create(session.Id, MessageRole.User,
            new List<MessagePart> { new TextPart("Message to delete") });
        await MessageService.CreateAsync(message);
        
        // Verify it exists
        var messages = await MessageService.GetBySessionAsync(session.Id);
        messages.Should().HaveCount(1);
        
        // Act
        await MessageService.DeleteAsync(message.Id);
        
        // Assert
        var messagesAfterDelete = await MessageService.GetBySessionAsync(session.Id);
        messagesAfterDelete.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatingAndDeletingMessages_ShouldUpdateSessionMessageCount()
    {
        // Arrange
        var session = await SessionService.CreateAsync("Count Session");

        // Initially zero
        var fresh = await SessionService.GetByIdAsync(session.Id);
        fresh!.MessageCount.Should().Be(0);

        var m1 = Message.Create(session.Id, MessageRole.User, new List<MessagePart>{ new TextPart("One") });
        var m2 = Message.Create(session.Id, MessageRole.Assistant, new List<MessagePart>{ new TextPart("Two") });
        var m3 = Message.Create(session.Id, MessageRole.User, new List<MessagePart>{ new TextPart("Three") });

        await MessageService.CreateAsync(m1);
        await MessageService.CreateAsync(m2);
        await MessageService.CreateAsync(m3);

        var afterCreates = await SessionService.GetByIdAsync(session.Id);
        afterCreates!.MessageCount.Should().Be(3);

        // Delete one
        await MessageService.DeleteAsync(m2.Id);
        var afterDelete = await SessionService.GetByIdAsync(session.Id);
        afterDelete!.MessageCount.Should().Be(2);

        // Delete remaining via bulk delete
        await MessageService.DeleteBySessionAsync(session.Id);
        var afterBulkDelete = await SessionService.GetByIdAsync(session.Id);
        afterBulkDelete!.MessageCount.Should().Be(0);
    }
}
