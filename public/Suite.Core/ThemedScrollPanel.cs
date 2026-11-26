namespace Suite.Core;

public sealed class ThemedScrollPanel : Panel
{
    protected override CreateParams CreateParams
    {
        get
        {
            SetStyle(ControlStyles.ApplyThemingImplicitly, true);
            return base.CreateParams;
        }
    }
}
