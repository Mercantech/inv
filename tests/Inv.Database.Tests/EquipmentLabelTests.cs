using System.Reflection;
using System.Text.Encodings.Web;
using Inv.Web.Components;
using Inv.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace Inv.Database.Tests;
public sealed class EquipmentLabelTests
{
    [Fact]
    public async Task Label_preview_uses_public_origin_and_explicit_connect_and_print_actions()
    {
        var bridge = new PrinterBridge();
        await using var provider = Services(bridge);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        EquipmentLabel? component = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<LabelProbe>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["ShortId"] = 109, ["Name"] = "Arduino Oplà", ["Capture"] = (Action<EquipmentLabel>)(c => component = c) })));
        async Task<string> Html() => await renderer.Dispatcher.InvokeAsync(() => System.Net.WebUtility.HtmlDecode(root.ToHtmlString()));
        async Task Act(string method) => await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var value = typeof(EquipmentLabel).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null);
            if (value is Task task) await task;
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null);
        });
        Assert.Equal("https://inv.mercantec.tech", bridge.PreparedBase);
        Assert.Contains("HTTPS://INV.MERCANTEC.TECH/E/109", await Html());
        Assert.Contains("Ingen printer forbundet", await Html());
        Assert.Equal(0, bridge.Prints);
        await Act("ConnectAsync"); Assert.Contains("Forbundet: Letratag Test", await Html());
        await Act("PrintAsync"); Assert.Equal(1, bridge.Prints); Assert.Equal(109, bridge.PrintedNumber);
        await Act("InvalidatePreview"); await Act("PrintAsync"); Assert.Equal(1, bridge.Prints);
    }
    [Fact]
    public async Task Unsupported_browser_and_cancelled_selection_are_visible_without_printing()
    {
        var bridge = new PrinterBridge { Supported = false, Cancel = true };
        await using var provider = Services(bridge);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        EquipmentLabel? component = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<LabelProbe>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["ShortId"] = 109, ["Name"] = "Test", ["Capture"] = (Action<EquipmentLabel>)(c => component = c) })));
        Assert.Contains("Bluetooth-print kræver Chrome eller Edge", await renderer.Dispatcher.InvokeAsync(() => System.Net.WebUtility.HtmlDecode(root.ToHtmlString())));
        bridge.Supported = true;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await (Task)typeof(EquipmentLabel).GetMethod("ConnectAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null)!;
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null);
        });
        Assert.Contains("Printervalget blev annulleret", await renderer.Dispatcher.InvokeAsync(() => System.Net.WebUtility.HtmlDecode(root.ToHtmlString())));
        Assert.Equal(0, bridge.Prints);
    }
    public sealed class LabelProbe : EquipmentLabel
    {
        [Parameter] public Action<EquipmentLabel> Capture { get; set; } = null!;
        protected override void OnInitialized() => Capture(this);
    }
    private static ServiceProvider Services(PrinterBridge bridge)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IJSRuntime>(bridge); services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton(new LabelOptions("https://inv.mercantec.tech")); services.AddScoped<LabelPrinterClient>();
        return services.BuildServiceProvider();
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost:3000/", "http://localhost:3000/udstyr/test");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class PrinterBridge : IJSRuntime, IJSObjectReference
    {
        public bool Supported = true, Cancel;
        public int Prints, PrintedNumber;
        public string? PreparedBase;
        private string? connected;
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => InvokeAsync<T>(id, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args)
        {
            object? result = id switch {
                "import" => this,
                "isSupported" => Supported,
                "connection" => connected,
                "prepare" => Prepare(args!),
                "connect" => Cancel ? throw new JSException("NotFoundError: User cancelled") : connected = "Letratag Test",
                "print" => Printed(args!),
                _ => throw new NotSupportedException(id)
            };
            return ValueTask.FromResult((T)result!);
        }
        private LabelPreview Prepare(object?[] args)
        {
            PreparedBase = (string)args[2]!;
            return new LabelPreview("data:image/png;base64,AA==", "HTTPS://INV.MERCANTEC.TECH/E/109", (int)args[0]!);
        }
        private string Printed(object?[] args) { Prints++; PrintedNumber = (int)args[0]!; return "Labelen er sendt."; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
