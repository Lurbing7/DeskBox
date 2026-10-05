using DeskBox.Contracts;
using DeskBox.Controls.WidgetContents;
using DeskBox.Models;
namespace DeskBox.Services;

internal sealed class SystemMonitorWidgetContentProvider : IWidgetContentProvider
{
    public WidgetKind WidgetKind => WidgetKind.SystemMonitor;
    public bool CanCreateDetachedContent => true;
    public IWidgetContent CreateDetachedContent(WidgetConfig config, WidgetContentProviderContext context) =>
        new SystemMonitorWidgetContent(config, context.LocalizationService, context.SettingsService);
}
