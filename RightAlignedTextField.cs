using Terminal.Gui;

class RightAlignedTextField : TextField
{
    public RightAlignedTextField(string text) : base(text)
    {
    }

    public override void Redraw(Rect bounds)
    {
        base.Redraw(bounds);

        int width = Frame.Width;
        if (width <= 0)
            return;

        string text = Text?.ToString() ?? string.Empty;
        if (text.Length > width)
            text = text[^width..];

        int padding = Math.Max(0, width - text.Length);

        Driver.SetAttribute(Enabled ? ColorScheme.Focus : ColorScheme.Disabled);
        Move(0, 0);
        Driver.AddStr(new string(' ', width));

        if (text.Length > 0)
        {
            Move(padding, 0);
            Driver.AddStr(text);
        }

        if (HasFocus)
            PositionCursor();
    }

    public override void PositionCursor()
    {
        int width = Frame.Width;
        if (!HasFocus || width <= 0)
            return;

        string text = Text?.ToString() ?? string.Empty;
        int visibleLength = Math.Min(text.Length, width);
        int hiddenLength = Math.Max(0, text.Length - width);
        int padding = Math.Max(0, width - visibleLength);
        int visibleCursor = Math.Clamp(CursorPosition - hiddenLength, 0, visibleLength);
        int cursorColumn = padding + visibleCursor;

        if (cursorColumn >= width)
            cursorColumn = width - 1;

        Move(cursorColumn, 0);
    }
}
