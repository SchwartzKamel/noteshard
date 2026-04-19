using System;
using System.Threading.Tasks;
using Microsoft.Windows.Widgets;

Console.WriteLine("=== Listing WidgetDefinitions via WidgetCatalog ===");
try
{
    var catalog = WidgetCatalog.GetDefault();
    var defs = catalog.GetWidgetDefinitions();
    Console.WriteLine($"Count: {defs.Length}");
    foreach (var d in defs)
    {
        Console.WriteLine($"- Id={d.Id}");
        Console.WriteLine($"  DisplayTitle={d.DisplayTitle}");
        Console.WriteLine($"  Description={d.Description}");
        Console.WriteLine($"  ProviderDefinition.Id={d.ProviderDefinition?.Id}");
        Console.WriteLine($"  ProviderDefinition.DisplayName={d.ProviderDefinition?.DisplayName}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"EX: {ex}");
}
