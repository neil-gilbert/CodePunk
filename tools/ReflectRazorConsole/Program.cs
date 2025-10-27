using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Components;

var asm = typeof(RazorConsole.Core.HostBuilderExtension).Assembly;
var type = asm.GetType("RazorConsole.Components.Markup", throwOnError:false);
Console.WriteLine($"Loaded: {asm.FullName}");
Console.WriteLine($"Markup type found: {type != null}");
if (type != null)
{
    var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => new {
            p.Name,
            Type = p.PropertyType.Name,
            IsParameter = p.GetCustomAttributes().Any(a => a.GetType().FullName == typeof(ParameterAttribute).FullName)
        })
        .OrderBy(p => p.Name)
        .ToList();
    Console.WriteLine("Properties:");
    foreach (var p in props)
        Console.WriteLine($" - {p.Name} : {p.Type} (Parameter={p.IsParameter})");
}
