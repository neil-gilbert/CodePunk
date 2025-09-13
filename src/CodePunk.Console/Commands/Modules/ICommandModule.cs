using System.CommandLine;

namespace CodePunk.Console.Commands.Modules;

internal interface ICommandModule
{
    void Register(RootCommand root, IServiceProvider services);
}
