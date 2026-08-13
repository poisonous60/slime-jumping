namespace SlimeJumping.UI.Framework
{
    /// Role tags panel controllers use to resolve children at Awake — lookup is by tag, not by
    /// child path, so rearranging the prefab hierarchy doesn't break code.
    public enum UISlotType
    {
        None = 0,

        // Structural
        ContentRoot = 1,
        Row = 2,
        Separator = 3,

        // Text / image
        Title = 10,
        Subtitle = 11,
        Body = 12,
        RowLabel = 13,
        ValueLabel = 14,
        Illustration = 15,

        // Common buttons
        CloseButton = 30,
        QuitButton = 31,
        ReturnButton = 32,
        StartButton = 33,
        OptionsButton = 34,
        PrevButton = 35,
        NextButton = 36,
        LoadButton = 37,

        // Controls
        Slider = 50,
        Dropdown = 51,

        // Options-panel row anchors — one per option row in the OptionsPanel prefab.
        Row_FrameLimit = 100,
        Row_PlayerSfx = 101,
        Row_WorldSfx = 102,
        Row_Bgm = 103,
        Row_Ui = 104,
        Row_WindowMode = 105,
        Row_Resolution = 106,
        Row_BackgroundCamera = 107,
        Row_VSync = 108,
        Row_ChargeVisual = 109,

        // GramophonePanel anchors
        ScrollViewport = 200,
        TrackListContent = 201,
        TrackRowTemplate = 202,
        StopButton = 203,
        CancelButton = 204,
    }
}
