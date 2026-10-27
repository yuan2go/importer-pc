using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyBarcodeProducerEditing(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string risk) { if (!ok) throw new InvalidOperationException(risk); lines.Add("PASS PRODUCER NATIVE " + risk); }
        var initial = new Ipc.Application.Sportmaster.SportmasterFlow().InitialConfiguration();
        var catalog = new Ipc.Assembly.ParcelCapabilityFactory(new(Path.Combine(root, "config/sportmaster"), evidence, null, null)).Catalog();
        var draft = new StartupConfigurationDraft(initial, catalog);
        var model = draft.CreateParameterEditor((p, c) => Task.FromResult(BarcodeFiltering.Select(p, c)));
        var left = draft.Readers[0]; var right = draft.Readers[1];
        var editor = new ParameterSettingsControl { Model = model };
        var window = new Window { Title = "条码生产者参数合成验证", Content = editor, Width = 1120, Height = 760 };
        window.Show();
        T Field<T>(string key) where T : FrameworkElement => Descendants((DependencyObject)window.Content).OfType<T>().Single(x => AutomationProperties.GetName(x) == left.InstanceId + "/" + key);
        try
        {
            editor.ShowPage(2, "左相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Check(Field<ComboBox>("entity.mode").SelectedItem?.ToString() == "observe-only" && left.Provider!.Implementation == "tcp-line",
                "daily form exposes the safe default and explicit business-mode choice");
            left.ConfigureLater = false;
            Field<TextBox>("device.ip").Text = "127.0.0.1";
            Field<TextBox>("device.port").Text = "1234";
            Field<TextBox>("camera.serial").Text = "SYNTHETIC-LEFT";
            Field<ComboBox>("profile").SelectedItem = "sequenced";
            Field<TextBox>("producer.id").Text = "synthetic.reader";
            Field<TextBox>("producer.version").Text = "1.0.0";
            Field<TextBox>("producer.config-hash").Text = new string('a', 64);
            Field<ComboBox>("entity.mode").SelectedItem = "trigger";
            Check(!model.ValidateFields() && model.Issues.Any(i => i.Path.EndsWith("/entity.evidence")), "missing entity/disconnect evidence is located before save review");
            Field<TextBox>("entity.evidence").Text = "Synthetic verified trigger boundary, full candidates and disconnected parcel inspection";
            Check(model.ValidateFields(), "complete explicit producer settings pass local form validation");
            CaptureInternal(evidence, "producer-daily-sequenced", editor, lines);
            var bytes = Wire.Encode(draft.Build());
            editor.ShowPage(2, "右相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(right.Parameters.Single(p => p.Field.Key == "entity.mode").Value == "observe-only", "left edits never enable right-side parcel admission");
            editor.ShowPage(2, "左相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(Wire.Encode(draft.Build()) == bytes && Field<TextBox>("entity.evidence").Text.Contains("Synthetic"), "navigation preserves the complete reviewed candidate and evidence");
            var startup = new ParameterSettingsControl { FirstConfiguration = true, Model = model };
            window.Content = startup; startup.ShowPage(2, "左相机"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Check(Field<ComboBox>("profile").SelectedItem?.ToString() == "sequenced" && ReferenceEquals(startup.Model, model), "first setup reuses the same explicit producer contract and candidate");
            CaptureInternal(evidence, "producer-first-sequenced", startup, lines);
            window.Width = 800; window.Height = 420; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Field<TextBox>("entity.evidence").BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(Field<TextBox>("entity.evidence").IsVisible && Field<TextBox>("entity.evidence").ActualWidth > 150 && Wire.Encode(draft.Build()) == bytes,
                "compact form can scroll to evidence without clipping the input or losing the candidate");
            CaptureInternal(evidence, "producer-first-compact-evidence", startup, lines);
        }
        finally { window.Close(); }
        lines.Add("BOUNDARY: HWND/Dispatcher and synthetic parameters only; no real camera, producer or RCS");
    }
}
