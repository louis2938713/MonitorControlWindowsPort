namespace MonitorControl.Core.Enums;

public enum MultiKeyboardBrightness
{
    Mouse = 0,
    AllScreens = 1,
    FocusInsteadOfMouse = 2,
}

public enum MultiKeyboardVolume
{
    Mouse = 0,
    AllScreens = 1,
    AudioDeviceNameMatching = 2,
}

public enum StartupAction
{
    DoNothing = 0,
    Write = 1,
    Read = 2,
}

public enum MultiSliders
{
    Separate = 0,
    Relevant = 1,
    Combine = 2,
}

public enum PollingMode
{
    None = -2,
    Minimal = -1,
    Normal = 0,
    Heavy = 1,
    Custom = 2,
}

public enum MenuIcon
{
    Show = 0,
    SliderOnly = 1,
    Hide = 2,
    ExternalOnly = 3,
}

public enum MenuItemStyle
{
    Icon = 0,
    Text = 1,
    Hide = 2,
}

public enum KeyboardBrightness
{
    Media = 0,
    Custom = 1,
    Both = 2,
    Disabled = 3,
}

public enum KeyboardVolume
{
    Media = 0,
    Custom = 1,
    Both = 2,
    Disabled = 3,
}
