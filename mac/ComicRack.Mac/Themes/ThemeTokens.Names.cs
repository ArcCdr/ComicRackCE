// The names of the tokens of ComicRackTheme.Light.axaml and ComicRackTheme.Dark.axaml (without the "Cr." prefix). ThemeTokenTests
// checks that this list and the two dictionaries agree.
namespace ComicRack.Mac.Themes
{
	public static partial class ThemeTokens
	{
		public static class Names
		{
			// System colours (KnownColorTableEx.cs)
			public const string ActiveBorder = "ActiveBorder";
			public const string ActiveCaption = "ActiveCaption";
			public const string ActiveCaptionText = "ActiveCaptionText";
			public const string AppWorkspace = "AppWorkspace";
			public const string Control = "Control";
			public const string ControlDark = "ControlDark";
			public const string ControlDarkDark = "ControlDarkDark";
			public const string ControlLight = "ControlLight";
			public const string ControlLightLight = "ControlLightLight";
			public const string ControlText = "ControlText";
			public const string Desktop = "Desktop";
			public const string GrayText = "GrayText";
			public const string Highlight = "Highlight";
			public const string HighlightText = "HighlightText";
			public const string HotTrack = "HotTrack";
			public const string InactiveBorder = "InactiveBorder";
			public const string InactiveCaption = "InactiveCaption";
			public const string InactiveCaptionText = "InactiveCaptionText";
			public const string Info = "Info";
			public const string InfoText = "InfoText";
			public const string Menu = "Menu";
			public const string MenuText = "MenuText";
			public const string ScrollBar = "ScrollBar";
			public const string Window = "Window";
			public const string WindowFrame = "WindowFrame";
			public const string WindowText = "WindowText";
			public const string ButtonFace = "ButtonFace";
			public const string ButtonHighlight = "ButtonHighlight";
			public const string ButtonShadow = "ButtonShadow";
			public const string GradientActiveCaption = "GradientActiveCaption";
			public const string GradientInactiveCaption = "GradientInactiveCaption";
			public const string MenuBar = "MenuBar";
			public const string WhiteSmoke = "WhiteSmoke";
			// App colours (ThemeColorTable.cs / DarkThemeColorTable.cs)
			public const string ComicBookLink = "ComicBookLink";
			public const string ComicBookVisitedLink = "ComicBookVisitedLink";
			public const string ComicBookPageViewerText = "ComicBookPageViewerText";
			public const string ComicBookPanelBack = "ComicBookPanelBack";
			public const string ComicBookWhereSeparator = "ComicBookWhereSeparator";
			public const string FavViewBack = "FavViewBack";
			public const string CollapsibleGroupBoxBack = "CollapsibleGroupBoxBack";
			public const string CollapsibleGroupBoxHeaderStart = "CollapsibleGroupBoxHeaderStart";
			public const string CollapsibleGroupBoxHeaderEnd = "CollapsibleGroupBoxHeaderEnd";
			public const string CollapsibleGroupBoxHeaderText = "CollapsibleGroupBoxHeaderText";
			public const string DeviceEditControlBack = "DeviceEditControlBack";
			public const string SmallPreviewPageViewerBack = "SmallPreviewPageViewerBack";
			public const string SmallPreviewPageViewerText = "SmallPreviewPageViewerText";
			public const string ThumbTileEmboss = "ThumbTileEmboss";
			public const string ThumbTileTitle = "ThumbTileTitle";
			public const string ThumbTileBody = "ThumbTileBody";
			public const string SelectionFocused = "SelectionFocused";
			public const string SelectionUnfocused = "SelectionUnfocused";
			public const string ThumbnailViewItemBack = "ThumbnailViewItemBack";
			public const string ThumbnailViewItemHighlightText = "ThumbnailViewItemHighlightText";
			public const string ThumbnailViewItemBorder = "ThumbnailViewItemBorder";
			public const string MagnifySetupBack = "MagnifySetupBack";
			public const string PrefsOverlayPanelBack = "PrefsOverlayPanelBack";
			public const string PrefsServerEditBack = "PrefsServerEditBack";
			public const string ItemViewMainBack = "ItemViewMainBack";
			public const string ItemViewDefaultBack = "ItemViewDefaultBack";
			public const string ItemViewGroupText = "ItemViewGroupText";
			public const string ItemViewGroupSeparator = "ItemViewGroupSeparator";
			public const string ItemViewMarker = "ItemViewMarker";
			public const string DetailRowHighlight = "DetailRowHighlight";
			public const string ItemDrawInfoText = "ItemDrawInfoText";
			public const string TreeDragSeparator = "TreeDragSeparator";
			public const string GaugeTotal = "GaugeTotal";
			public const string GaugeTotalText = "GaugeTotalText";
			public const string GaugeUnread = "GaugeUnread";
			public const string GaugeUnreadText = "GaugeUnreadText";
			public const string GaugeNew = "GaugeNew";
			public const string GaugeNewText = "GaugeNewText";
			public const string SmartQueryBack = "SmartQueryBack";
			public const string SmartQueryException = "SmartQueryException";
			public const string SmartQueryText = "SmartQueryText";
			public const string SmartQueryKeyword = "SmartQueryKeyword";
			public const string SmartQueryQualifier = "SmartQueryQualifier";
			public const string SmartQueryNegation = "SmartQueryNegation";
			public const string SmartQueryString = "SmartQueryString";
			public const string TabBarBack = "TabBarBack";
			public const string TabBarDefaultBorder = "TabBarDefaultBorder";
			public const string TabBarSelectedBack = "TabBarSelectedBack";
			public const string ToolTipText = "ToolTipText";
			public const string ComboBoxSeparator = "ComboBoxSeparator";
			public const string CaptionBack = "CaptionBack";
			public const string CaptionText = "CaptionText";
			public const string HeaderBack = "HeaderBack";
			public const string HeaderSeparator = "HeaderSeparator";
			public const string RatingUnrated = "RatingUnrated";
			public const string RatingRated = "RatingRated";
			public const string SplitButtonSeparatorLeft = "SplitButtonSeparatorLeft";
			public const string SplitButtonSeparatorRight = "SplitButtonSeparatorRight";
			public const string StackFill = "StackFill";
			public const string StackBorder = "StackBorder";
			public const string ReaderBackground = "ReaderBackground";
			public const string VistaColor = "VistaColor";
			// Dark-only colours (DarkColors.cs)
			public const string SidePanel = "SidePanel";
			public const string Content = "Content";
			public const string ListBack = "ListBack";
			public const string EditBack = "EditBack";
			public const string EditHoverBack = "EditHoverBack";
			public const string EditFocusBack = "EditFocusBack";
			public const string BorderDarkest = "BorderDarkest";
			public const string BorderDark = "BorderDark";
			public const string BorderDefault = "BorderDefault";
			public const string BorderLight = "BorderLight";
			public const string SelectionHighlight = "SelectionHighlight";
			public const string Focus = "Focus";
			// Control colours (light: chrome-guidelines 2.5, assumed; dark: DarkColors.cs)
			public const string ButtonBack = "ButtonBack";
			public const string ButtonBorder = "ButtonBorder";
			public const string ButtonHoverBack = "ButtonHoverBack";
			public const string ButtonHoverBorder = "ButtonHoverBorder";
			public const string ButtonPressedBack = "ButtonPressedBack";
			public const string ButtonPressedBorder = "ButtonPressedBorder";
			public const string ButtonDefaultBorder = "ButtonDefaultBorder";
			public const string CheckBoxBack = "CheckBoxBack";
			public const string CheckBoxBorder = "CheckBoxBorder";
			public const string CheckBoxHoverBorder = "CheckBoxHoverBorder";
			public const string CheckBoxCheck = "CheckBoxCheck";
			public const string CheckBoxAccent = "CheckBoxAccent";
			public const string EditFrame = "EditFrame";
			public const string EditHoverFrame = "EditHoverFrame";
			public const string EditFocusFrame = "EditFocusFrame";
			public const string TrackBarTrack = "TrackBarTrack";
			public const string TrackBarTrackBorder = "TrackBarTrackBorder";
			public const string TrackBarThumb = "TrackBarThumb";
			public const string TabPageSelected = "TabPageSelected";
			public const string TabPageOther = "TabPageOther";
			public const string TabPageBorder = "TabPageBorder";
			// Toolstrip and menu colours (ProfessionalColorTable, computed by ProfessionalColors.cs)
			public const string ToolButtonSelectedHighlight = "ToolButtonSelectedHighlight";
			public const string ToolButtonSelectedHighlightBorder = "ToolButtonSelectedHighlightBorder";
			public const string ToolButtonPressedHighlight = "ToolButtonPressedHighlight";
			public const string ToolButtonPressedHighlightBorder = "ToolButtonPressedHighlightBorder";
			public const string ToolButtonCheckedHighlight = "ToolButtonCheckedHighlight";
			public const string ToolButtonCheckedHighlightBorder = "ToolButtonCheckedHighlightBorder";
			public const string ToolButtonPressedBorder = "ToolButtonPressedBorder";
			public const string ToolButtonSelectedBorder = "ToolButtonSelectedBorder";
			public const string ToolButtonCheckedGradientBegin = "ToolButtonCheckedGradientBegin";
			public const string ToolButtonCheckedGradientMiddle = "ToolButtonCheckedGradientMiddle";
			public const string ToolButtonCheckedGradientEnd = "ToolButtonCheckedGradientEnd";
			public const string ToolButtonSelectedGradientBegin = "ToolButtonSelectedGradientBegin";
			public const string ToolButtonSelectedGradientMiddle = "ToolButtonSelectedGradientMiddle";
			public const string ToolButtonSelectedGradientEnd = "ToolButtonSelectedGradientEnd";
			public const string ToolButtonPressedGradientBegin = "ToolButtonPressedGradientBegin";
			public const string ToolButtonPressedGradientMiddle = "ToolButtonPressedGradientMiddle";
			public const string ToolButtonPressedGradientEnd = "ToolButtonPressedGradientEnd";
			public const string ToolCheckBackground = "ToolCheckBackground";
			public const string ToolCheckSelectedBackground = "ToolCheckSelectedBackground";
			public const string ToolCheckPressedBackground = "ToolCheckPressedBackground";
			public const string ToolGripDark = "ToolGripDark";
			public const string ToolGripLight = "ToolGripLight";
			public const string ToolImageMarginGradientBegin = "ToolImageMarginGradientBegin";
			public const string ToolImageMarginGradientMiddle = "ToolImageMarginGradientMiddle";
			public const string ToolImageMarginGradientEnd = "ToolImageMarginGradientEnd";
			public const string ToolImageMarginRevealedGradientBegin = "ToolImageMarginRevealedGradientBegin";
			public const string ToolImageMarginRevealedGradientMiddle = "ToolImageMarginRevealedGradientMiddle";
			public const string ToolImageMarginRevealedGradientEnd = "ToolImageMarginRevealedGradientEnd";
			public const string ToolMenuStripGradientBegin = "ToolMenuStripGradientBegin";
			public const string ToolMenuStripGradientEnd = "ToolMenuStripGradientEnd";
			public const string ToolMenuItemSelected = "ToolMenuItemSelected";
			public const string ToolMenuItemBorder = "ToolMenuItemBorder";
			public const string ToolMenuBorder = "ToolMenuBorder";
			public const string ToolMenuItemSelectedGradientBegin = "ToolMenuItemSelectedGradientBegin";
			public const string ToolMenuItemSelectedGradientEnd = "ToolMenuItemSelectedGradientEnd";
			public const string ToolMenuItemPressedGradientBegin = "ToolMenuItemPressedGradientBegin";
			public const string ToolMenuItemPressedGradientMiddle = "ToolMenuItemPressedGradientMiddle";
			public const string ToolMenuItemPressedGradientEnd = "ToolMenuItemPressedGradientEnd";
			public const string ToolRaftingContainerGradientBegin = "ToolRaftingContainerGradientBegin";
			public const string ToolRaftingContainerGradientEnd = "ToolRaftingContainerGradientEnd";
			public const string ToolSeparatorDark = "ToolSeparatorDark";
			public const string ToolSeparatorLight = "ToolSeparatorLight";
			public const string ToolStatusStripBorder = "ToolStatusStripBorder";
			public const string ToolStatusStripGradientBegin = "ToolStatusStripGradientBegin";
			public const string ToolStatusStripGradientEnd = "ToolStatusStripGradientEnd";
			public const string ToolToolStripBorder = "ToolToolStripBorder";
			public const string ToolToolStripDropDownBackground = "ToolToolStripDropDownBackground";
			public const string ToolToolStripGradientBegin = "ToolToolStripGradientBegin";
			public const string ToolToolStripGradientMiddle = "ToolToolStripGradientMiddle";
			public const string ToolToolStripGradientEnd = "ToolToolStripGradientEnd";
			public const string ToolToolStripContentPanelGradientBegin = "ToolToolStripContentPanelGradientBegin";
			public const string ToolToolStripContentPanelGradientEnd = "ToolToolStripContentPanelGradientEnd";
			public const string ToolToolStripPanelGradientBegin = "ToolToolStripPanelGradientBegin";
			public const string ToolToolStripPanelGradientEnd = "ToolToolStripPanelGradientEnd";
			public const string ToolOverflowButtonGradientBegin = "ToolOverflowButtonGradientBegin";
			public const string ToolOverflowButtonGradientMiddle = "ToolOverflowButtonGradientMiddle";
			public const string ToolOverflowButtonGradientEnd = "ToolOverflowButtonGradientEnd";
		}

		/// <summary>Tokens that exist only in the dark dictionary (colours of the original dark theme without a light counterpart).</summary>
		public static readonly string[] DarkOnly =
		{
			Names.SidePanel,
			Names.Content,
			Names.ListBack,
			Names.EditBack,
			Names.EditHoverBack,
			Names.EditFocusBack,
			Names.BorderDarkest,
			Names.BorderDark,
			Names.BorderDefault,
			Names.BorderLight,
			Names.SelectionHighlight,
			Names.Focus,
		};
	}
}
