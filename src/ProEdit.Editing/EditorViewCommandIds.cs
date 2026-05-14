namespace ProEdit.Editing;

public static class EditorViewCommandIds
{
    public static class Views
    {
        public const string ReadMode = "view.views.readMode";
        public const string PrintLayout = "view.views.printLayout";
        public const string WebLayout = "view.views.webLayout";
        public const string Outline = "view.views.outline";
        public const string Draft = "view.views.draft";
    }

    public static class Show
    {
        public const string Ruler = "view.show.ruler";
        public const string Gridlines = "view.show.gridlines";
        public const string NavigationPane = "view.show.navigationPane";
    }

    public static class PageMovement
    {
        public const string Vertical = "view.pageMovement.vertical";
        public const string SideToSide = "view.pageMovement.sideToSide";
    }

    public static class Zoom
    {
        public const string ZoomDialog = "view.zoom.dialog";
        public const string Zoom100 = "view.zoom.100";
        public const string OnePage = "view.zoom.onePage";
        public const string MultiplePages = "view.zoom.multiplePages";
        public const string PageWidth = "view.zoom.pageWidth";
    }

    public static class Window
    {
        public const string NewWindow = "view.window.new";
        public const string ArrangeAll = "view.window.arrangeAll";
        public const string Split = "view.window.split";
        public const string ViewSideBySide = "view.window.sideBySide";
        public const string SynchronousScrolling = "view.window.syncScroll";
        public const string ResetWindowPosition = "view.window.resetPosition";
        public const string SwitchWindows = "view.window.switch";
    }

    public static class Macros
    {
        public const string Open = "view.macros.macros";
        public const string RecordMacro = "view.macros.record";
        public const string VbaEditor = "view.macros.vbaEditor";
        public const string Debug = "view.macros.debug";
    }
}
