namespace Ecliptix.Core.Utilities;

/// <summary>
/// Provides easy access to Phosphor Icons (Light weight) SVG paths.
/// All icons are located in Assets/Icons/ directory.
/// </summary>
public static class PhosphorIcons
{
    private const string IconBasePath = "avares://Ecliptix.Core/Assets/Icons/";

    /// <summary>
    /// Gets the full Avalonia resource path for a Phosphor icon.
    /// </summary>
    /// <param name="iconName">The icon name without extension (e.g., "user", "home", "settings")</param>
    /// <returns>Full avares:// path to the SVG icon</returns>
    public static string GetIconPath(string iconName) => $"{IconBasePath}{iconName}-light.svg";

    // Common UI Icons
    public static class Common
    {
        public static string User => GetIconPath("user");
        public static string UserCircle => GetIconPath("user-circle");
        public static string Users => GetIconPath("users");
        public static string Home => GetIconPath("house");
        public static string Settings => GetIconPath("gear");
        public static string SettingsFine => GetIconPath("gear-fine");
        public static string Bell => GetIconPath("bell");
        public static string BellRinging => GetIconPath("bell-ringing");
        public static string Search => GetIconPath("magnifying-glass");
        public static string Plus => GetIconPath("plus");
        public static string Minus => GetIconPath("minus");
        public static string X => GetIconPath("x");
        public static string Check => GetIconPath("check");
        public static string Info => GetIconPath("info");
        public static string Warning => GetIconPath("warning");
        public static string WarningCircle => GetIconPath("warning-circle");
        public static string Question => GetIconPath("question");
        public static string QuestionMark => GetIconPath("question-mark");
    }

    // Navigation Icons
    public static class Navigation
    {
        public static string ArrowLeft => GetIconPath("arrow-left");
        public static string ArrowRight => GetIconPath("arrow-right");
        public static string ArrowUp => GetIconPath("arrow-up");
        public static string ArrowDown => GetIconPath("arrow-down");
        public static string CaretLeft => GetIconPath("caret-left");
        public static string CaretRight => GetIconPath("caret-right");
        public static string CaretUp => GetIconPath("caret-up");
        public static string CaretDown => GetIconPath("caret-down");
        public static string ChevronLeft => GetIconPath("caret-circle-left");
        public static string ChevronRight => GetIconPath("caret-circle-right");
        public static string ChevronUp => GetIconPath("caret-circle-up");
        public static string ChevronDown => GetIconPath("caret-circle-down");
        public static string Menu => GetIconPath("list");
        public static string MenuBurger => GetIconPath("list-dashes");
        public static string DotsThree => GetIconPath("dots-three");
        public static string DotsThreeVertical => GetIconPath("dots-three-vertical");
    }

    // Communication Icons
    public static class Communication
    {
        public static string Chat => GetIconPath("chat");
        public static string ChatCircle => GetIconPath("chat-circle");
        public static string ChatCentered => GetIconPath("chat-centered");
        public static string ChatTeardrop => GetIconPath("chat-teardrop");
        public static string Chats => GetIconPath("chats");
        public static string ChatsCircle => GetIconPath("chats-circle");
        public static string Envelope => GetIconPath("envelope");
        public static string EnvelopeSimple => GetIconPath("envelope-simple");
        public static string Phone => GetIconPath("phone");
        public static string PhoneCall => GetIconPath("phone-call");
        public static string VideoCamera => GetIconPath("video-camera");
        public static string Microphone => GetIconPath("microphone");
        public static string MicrophoneSlash => GetIconPath("microphone-slash");
        public static string PaperPlane => GetIconPath("paper-plane-tilt");
        public static string ShareNetwork => GetIconPath("share-network");
    }

    // File & Document Icons
    public static class Documents
    {
        public static string File => GetIconPath("file");
        public static string FileText => GetIconPath("file-text");
        public static string Files => GetIconPath("files");
        public static string Folder => GetIconPath("folder");
        public static string FolderOpen => GetIconPath("folder-open");
        public static string FolderPlus => GetIconPath("folder-plus");
        public static string Image => GetIconPath("image");
        public static string Images => GetIconPath("images");
        public static string Download => GetIconPath("download");
        public static string DownloadSimple => GetIconPath("download-simple");
        public static string Upload => GetIconPath("upload");
        public static string UploadSimple => GetIconPath("upload-simple");
        public static string Cloud => GetIconPath("cloud");
        public static string CloudArrowDown => GetIconPath("cloud-arrow-down");
        public static string CloudArrowUp => GetIconPath("cloud-arrow-up");
    }

    // Media Controls
    public static class Media
    {
        public static string Play => GetIconPath("play");
        public static string PlayCircle => GetIconPath("play-circle");
        public static string Pause => GetIconPath("pause");
        public static string PauseCircle => GetIconPath("pause-circle");
        public static string Stop => GetIconPath("stop");
        public static string StopCircle => GetIconPath("stop-circle");
        public static string SkipBack => GetIconPath("skip-back");
        public static string SkipForward => GetIconPath("skip-forward");
        public static string SpeakerHigh => GetIconPath("speaker-high");
        public static string SpeakerLow => GetIconPath("speaker-low");
        public static string SpeakerSlash => GetIconPath("speaker-slash");
    }

    // Security & Lock Icons
    public static class Security
    {
        public static string Lock => GetIconPath("lock");
        public static string LockOpen => GetIconPath("lock-open");
        public static string LockKey => GetIconPath("lock-key");
        public static string Key => GetIconPath("key");
        public static string Shield => GetIconPath("shield");
        public static string ShieldCheck => GetIconPath("shield-check");
        public static string ShieldWarning => GetIconPath("shield-warning");
        public static string Eye => GetIconPath("eye");
        public static string EyeSlash => GetIconPath("eye-slash");
        public static string Fingerprint => GetIconPath("fingerprint");
    }

    // Editor & Text Icons
    public static class Editor
    {
        public static string TextAa => GetIconPath("text-aa");
        public static string TextBolder => GetIconPath("text-bolder");
        public static string TextItalic => GetIconPath("text-italic");
        public static string TextUnderline => GetIconPath("text-underline");
        public static string TextStrikethrough => GetIconPath("text-strikethrough");
        public static string Link => GetIconPath("link");
        public static string LinkBreak => GetIconPath("link-break");
        public static string Paperclip => GetIconPath("paperclip");
        public static string Code => GetIconPath("code");
        public static string CodeBlock => GetIconPath("code-block");
        public static string ListBullets => GetIconPath("list-bullets");
        public static string ListNumbers => GetIconPath("list-numbers");
    }

    // Action Icons
    public static class Actions
    {
        public static string Trash => GetIconPath("trash");
        public static string TrashSimple => GetIconPath("trash-simple");
        public static string Pencil => GetIconPath("pencil");
        public static string PencilSimple => GetIconPath("pencil-simple");
        public static string Copy => GetIconPath("copy");
        public static string Clipboard => GetIconPath("clipboard");
        public static string ClipboardText => GetIconPath("clipboard-text");
        public static string FloppyDisk => GetIconPath("floppy-disk");
        public static string Heart => GetIconPath("heart");
        public static string Star => GetIconPath("star");
        public static string Bookmark => GetIconPath("bookmark");
        public static string BookmarkSimple => GetIconPath("bookmark-simple");
        public static string Flag => GetIconPath("flag");
        public static string FlagBanner => GetIconPath("flag-banner");
    }

    // Status & State Icons
    public static class Status
    {
        public static string CheckCircle => GetIconPath("check-circle");
        public static string XCircle => GetIconPath("x-circle");
        public static string WarningCircle => GetIconPath("warning-circle");
        public static string InfoCircle => GetIconPath("info");
        public static string Spinner => GetIconPath("spinner");
        public static string SpinnerGap => GetIconPath("spinner-gap");
        public static string CircleNotch => GetIconPath("circle-notch");
        public static string Hourglass => GetIconPath("hourglass");
        public static string HourglassHigh => GetIconPath("hourglass-high");
    }

    // System & App Icons
    public static class System
    {
        public static string Moon => GetIconPath("moon");
        public static string Sun => GetIconPath("sun");
        public static string Monitor => GetIconPath("monitor");
        public static string Desktop => GetIconPath("desktop");
        public static string DeviceMobile => GetIconPath("device-mobile");
        public static string Laptop => GetIconPath("laptop");
        public static string Battery => GetIconPath("battery-full");
        public static string BatteryCharging => GetIconPath("battery-charging");
        public static string Wifi => GetIconPath("wifi-high");
        public static string WifiSlash => GetIconPath("wifi-slash");
        public static string SignOut => GetIconPath("sign-out");
        public static string SignIn => GetIconPath("sign-in");
        public static string Power => GetIconPath("power");
    }

    // Social & Sharing
    public static class Social
    {
        public static string At => GetIconPath("at");
        public static string Hash => GetIconPath("hash");
        public static string ShareFat => GetIconPath("share-fat");
        public static string Export => GetIconPath("export");
        public static string UserPlus => GetIconPath("user-plus");
        public static string UserMinus => GetIconPath("user-minus");
        public static string UsersThree => GetIconPath("users-three");
        public static string Handshake => GetIconPath("handshake");
    }

    // Calendar & Time
    public static class Time
    {
        public static string Calendar => GetIconPath("calendar");
        public static string CalendarBlank => GetIconPath("calendar-blank");
        public static string Clock => GetIconPath("clock");
        public static string ClockClockwise => GetIconPath("clock-clockwise");
        public static string ClockCounterClockwise => GetIconPath("clock-counter-clockwise");
        public static string Timer => GetIconPath("timer");
        public static string Alarm => GetIconPath("alarm");
    }
}
