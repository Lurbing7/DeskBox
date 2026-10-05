using DeskBox.Contracts;
using DeskBox.Controls.WidgetContents;
using DeskBox.Models;
namespace DeskBox.Services;
internal sealed class DockWidgetContentProvider : IWidgetContentProvider
{
    public WidgetKind WidgetKind => WidgetKind.Dock;
    public bool CanCreateDetachedContent => true;
    public IWidgetContent CreateDetachedContent(WidgetConfig config, WidgetContentProviderContext context) => new DockWidgetContent(config, context.LocalizationService);
}
