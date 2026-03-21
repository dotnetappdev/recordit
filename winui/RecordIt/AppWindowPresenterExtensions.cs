using Microsoft.UI.Windowing;

namespace Microsoft.UI.Windowing
{
    public static class AppWindowPresenterExtensions
    {
        // Provide a no-op SetPresenter extension for compatibility with different SDK versions
        public static void SetPresenter(this AppWindowPresenter presenter, AppWindowPresenterKind kind) { }
    }
}
