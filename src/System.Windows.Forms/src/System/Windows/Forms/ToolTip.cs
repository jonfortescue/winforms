﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;

namespace System.Windows.Forms
{
    /// <summary>
    /// Provides a small pop-up window containing a line of text that describes the purpose of a
    /// tool or control (usually represented as a graphical object) in a program.
    /// </summary>
    [ProvideProperty(nameof(ToolTip), typeof(Control))]
    [DefaultEvent(nameof(Popup))]
    [ToolboxItemFilter("System.Windows.Forms")]
    [SRDescription(nameof(SR.DescriptionToolTip))]
    public class ToolTip : Component, IExtenderProvider
    {
        private const int DefaultDelay = 500;
        private const int ReshowRatio = 5;
        private const int AutoPopRatio = 10;

        private const int BalloonOffsetX = 10;
        private const int BaloonOffsetY = 8;

        private const int LocationIndexTop = 0;
        private const int LocationIndexRight = 1;
        private const int LocationIndexBottom = 2;
        private const int LocationIndexLeft = 3;
        private const int LocationTotal = 4;
        private readonly Hashtable _tools = new Hashtable();
        private readonly int[] _delayTimes = new int[4];
        private bool _auto = true;
        private bool _showAlways = false;
        private ToolTipNativeWindow _window = null;
        private Control _topLevelControl = null;
        private bool active = true;
        private Color _backColor = SystemColors.Info;
        private Color _foreColor = SystemColors.InfoText;
        private bool _isBalloon;
        private bool _isDisposing;
        private string _toolTipTitle = string.Empty;
        private ToolTipIcon _toolTipIcon = ToolTipIcon.None;
        private ToolTipTimer _timer;
        private readonly Hashtable _owners = new Hashtable();
        private bool _stripAmpersands = false;
        private bool _useAnimation = true;
        private bool _useFading = true;
        private int _originalPopupDelay = 0;

        /// <summary>
        /// Setting TTM_TRACKPOSITION will cause redundant POP and Draw Messages.
        /// Hence we guard against this by having this private flag.
        /// </summary>
        private bool _trackPosition = false;

        private PopupEventHandler _onPopup;
        private DrawToolTipEventHandler _onDraw;

        /// <summary>
        /// Adding a tool twice breaks the ToolTip, so we need to track which
        /// tools are created to prevent this.
        /// </summary>
        private readonly Hashtable _created = new Hashtable();

        private bool _cancelled = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolTip"/> class, given the container.
        /// </summary>
        public ToolTip(IContainer cont) : this()
        {
            if (cont == null)
            {
                throw new ArgumentNullException(nameof(cont));
            }

            cont.Add(this);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolTip"/> class in its default state.
        /// </summary>
        public ToolTip()
        {
            _window = new ToolTipNativeWindow(this);
            _auto = true;
            _delayTimes[NativeMethods.TTDT_AUTOMATIC] = DefaultDelay;
            AdjustBaseFromAuto();
        }

        /// <summary>
        /// Gets or sets a value indicating whether the <see cref="ToolTip"/> control is currently active.
        /// </summary>
        [SRDescription(nameof(SR.ToolTipActiveDescr))]
        [DefaultValue(true)]
        public bool Active
        {
            get => active;
            set
            {
                if (active != value)
                {
                    active = value;

                    // Don't activate the tooltip if we're in the designer.
                    if (!DesignMode && GetHandleCreated())
                    {
                        UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_ACTIVATE, (value == true) ? 1 : 0, 0);
                    }
                }
            }
        }

        internal void HideToolTip(IKeyboardToolTip currentTool)
        {
            Hide(currentTool.GetOwnerWindow());
        }

        /// <summary>
        /// Gets or sets the time (in milliseconds) that passes before the <see cref="ToolTip"/> appears.
        /// </summary>
        [RefreshProperties(RefreshProperties.All)]
        [SRDescription(nameof(SR.ToolTipAutomaticDelayDescr))]
        [DefaultValue(DefaultDelay)]
        public int AutomaticDelay
        {
            get => _delayTimes[NativeMethods.TTDT_AUTOMATIC];
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, string.Format(SR.InvalidLowBoundArgumentEx, nameof(AutomaticDelay), value, 0));
                }

                SetDelayTime(NativeMethods.TTDT_AUTOMATIC, value);
            }
        }

        internal string GetCaptionForTool(Control tool)
        {
            Debug.Assert(tool != null, "tool should not be null");
            return ((TipInfo)_tools[tool])?.Caption;
        }

        /// <summary>
        /// Gets or sets the initial delay for the <see cref="ToolTip"/> control.
        /// </summary>
        [RefreshProperties(RefreshProperties.All)]
        [SRDescription(nameof(SR.ToolTipAutoPopDelayDescr))]
        public int AutoPopDelay
        {
            get => _delayTimes[NativeMethods.TTDT_AUTOPOP];
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, string.Format(SR.InvalidLowBoundArgumentEx, nameof(AutoPopDelay), value, 0));
                }

                SetDelayTime(NativeMethods.TTDT_AUTOPOP, value);
            }
        }

        /// <summary>
        /// Gets or sets the BackColor for the <see cref="ToolTip"/> control.
        /// </summary>
        [SRDescription(nameof(SR.ToolTipBackColorDescr))]
        [DefaultValue(typeof(Color), "Info")]
        public Color BackColor
        {
            get => _backColor;
            set
            {
                _backColor = value;
                if (GetHandleCreated())
                {
                    UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETTIPBKCOLOR, ColorTranslator.ToWin32(_backColor), 0);
                }
            }
        }

        /// <summary>
        /// The CreateParams to create the window.
        /// </summary>
        protected virtual CreateParams CreateParams
        {
            get
            {
                CreateParams cp = new CreateParams();
                if (TopLevelControl != null && !TopLevelControl.IsDisposed)
                {
                    cp.Parent = TopLevelControl.Handle;
                }
                cp.ClassName = NativeMethods.TOOLTIPS_CLASS;
                if (_showAlways)
                {
                    cp.Style = NativeMethods.TTS_ALWAYSTIP;
                }
                if (_isBalloon)
                {
                    cp.Style |= NativeMethods.TTS_BALLOON;
                }
                if (!_stripAmpersands)
                {
                    cp.Style |= NativeMethods.TTS_NOPREFIX;
                }
                if (!_useAnimation)
                {
                    cp.Style |= NativeMethods.TTS_NOANIMATE;
                }
                if (!_useFading)
                {
                    cp.Style |= NativeMethods.TTS_NOFADE;
                }
                cp.ExStyle = 0;
                cp.Caption = null;

                return cp;
            }
        }

        /// <summary>
        /// Gets or sets the ForeColor for the <see cref="ToolTip"/> control.
        /// </summary>
        [SRDescription(nameof(SR.ToolTipForeColorDescr))]
        [DefaultValue(typeof(Color), "InfoText")]
        public Color ForeColor
        {
            get => _foreColor;
            set
            {
                if (value.IsEmpty)
                {
                    throw new ArgumentException(string.Format(SR.ToolTipEmptyColor, nameof(ForeColor)), nameof(value));
                }

                _foreColor = value;
                if (GetHandleCreated())
                {
                    UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETTIPTEXTCOLOR, ColorTranslator.ToWin32(_foreColor), 0);
                }
            }
        }

        internal IntPtr Handle
        {
            get
            {
                if (!GetHandleCreated())
                {
                    CreateHandle();
                }

                return _window.Handle;
            }
        }

        /// <summary>
        /// Gets or sets the IsBalloon for the <see cref="ToolTip"/> control.
        /// </summary>
        [SRDescription(nameof(SR.ToolTipIsBalloonDescr))]
        [DefaultValue(false)]
        public bool IsBalloon
        {
            get => _isBalloon;
            set
            {
                if (_isBalloon != value)
                {
                    _isBalloon = value;
                    if (GetHandleCreated())
                    {
                        RecreateHandle();
                    }
                }
            }
        }

        private bool IsWindowActive(IWin32Window window)
        {
            // We want to enter in the if block only if ShowParams does not return SW_SHOWNOACTIVATE.
            // for ToolStripDropDown ShowParams returns SW_SHOWNOACTIVATE, in which case we don't
            // want to check IsWindowActive and hence return true.
            if (window is Control windowControl &&
                (windowControl.ShowParams & 0xF) != NativeMethods.SW_SHOWNOACTIVATE)
            {
                IntPtr hWnd = UnsafeNativeMethods.GetActiveWindow();
                IntPtr rootHwnd = UnsafeNativeMethods.GetAncestor(new HandleRef(window, window.Handle), NativeMethods.GA_ROOT);
                if (hWnd != rootHwnd)
                {
                    TipInfo tt = (TipInfo)_tools[windowControl];
                    if (tt != null && (tt.TipType & TipInfo.Type.SemiAbsolute) != 0)
                    {
                        _tools.Remove(windowControl);
                        DestroyRegion(windowControl);
                    }

                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets or sets the initial delay for the <see cref="ToolTip"/> control.
        /// </summary>
        [RefreshProperties(RefreshProperties.All)]
        [Description(nameof(SR.ToolTipInitialDelayDescr))]
        public int InitialDelay
        {
            get => _delayTimes[NativeMethods.TTDT_INITIAL];
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, string.Format(SR.InvalidLowBoundArgumentEx, nameof(InitialDelay), value, 0));
                }

                SetDelayTime(NativeMethods.TTDT_INITIAL, value);
            }
        }

        /// <summary>
        /// Indicates whether the ToolTip will be drawn by the system or the user.
        /// </summary>
        [SRCategory(nameof(SR.CatBehavior))]
        [DefaultValue(false)]
        [Description(nameof(SR.ToolTipOwnerDrawDescr))]
        public bool OwnerDraw { get; set; }

        /// <summary>
        /// Gets or sets the length of time (in milliseconds) that it takes subsequent ToolTip
        /// instances to appear as the mouse pointer moves from one ToolTip region to another.
        /// </summary>
        [RefreshProperties(RefreshProperties.All)]
        [Description(nameof(SR.ToolTipReshowDelayDescr))]
        public int ReshowDelay
        {
            get => _delayTimes[NativeMethods.TTDT_RESHOW];
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, string.Format(SR.InvalidLowBoundArgumentEx, nameof(ReshowDelay), value, 0));
                }

                SetDelayTime(NativeMethods.TTDT_RESHOW, value);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the <see cref="ToolTip"/> appears even when its
        /// parent control is not active.
        /// </summary>
        [DefaultValue(false)]
        [Description(nameof(SR.ToolTipShowAlwaysDescr))]
        public bool ShowAlways
        {
            get => _showAlways;
            set
            {
                if (_showAlways != value)
                {
                    _showAlways = value;
                    if (GetHandleCreated())
                    {
                        RecreateHandle();
                    }
                }
            }
        }

        /// <summary>
        /// When set to true, any ampersands in the Text property are not displayed.
        /// </summary>
        [SRDescription(nameof(SR.ToolTipStripAmpersandsDescr))]
        [Browsable(true)]
        [DefaultValue(false)]
        public bool StripAmpersands
        {
            get => _stripAmpersands;
            set
            {
                if (_stripAmpersands != value)
                {
                    _stripAmpersands = value;
                    if (GetHandleCreated())
                    {
                        RecreateHandle();
                    }
                }
            }
        }

        [SRCategory(nameof(SR.CatData))]
        [Localizable(false)]
        [Bindable(true)]
        [Description(nameof(SR.ControlTagDescr))]
        [DefaultValue(null),
        TypeConverter(typeof(StringConverter))]
        public object Tag { get; set; }

        /// <summary>
        /// Gets or sets an Icon on the ToolTip.
        /// </summary>
        [DefaultValue(ToolTipIcon.None)]
        [Description(nameof(SR.ToolTipToolTipIconDescr))]
        public ToolTipIcon ToolTipIcon
        {
            get => _toolTipIcon;
            set
            {
                if (_toolTipIcon != value)
                {
                    if (!ClientUtils.IsEnumValid(value, (int)value, (int)ToolTipIcon.None, (int)ToolTipIcon.Error))
                    {
                        throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ToolTipIcon));
                    }

                    _toolTipIcon = value;
                    if (_toolTipIcon > 0 && GetHandleCreated())
                    {
                        // If the title is null/empty, the icon won't display.
                        string title = !string.IsNullOrEmpty(_toolTipTitle) ? _toolTipTitle : " ";
                        UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETTITLE, (int)_toolTipIcon, title);

                        // Tooltip need to be updated to reflect the changes in the icon because
                        // this operation directly affects the size of the tooltip
                        UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_UPDATE, 0, 0);
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the title of the ToolTip.
        /// </summary>
        [DefaultValue("")]
        [Description(nameof(SR.ToolTipTitleDescr))]
        public string ToolTipTitle
        {
            get => _toolTipTitle;
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }

                if (_toolTipTitle != value)
                {
                    _toolTipTitle = value;
                    if (GetHandleCreated())
                    {
                        UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETTITLE, (int)_toolTipIcon, _toolTipTitle);

                        // Tooltip need to be updated to reflect the changes in the titletext because
                        // this operation directly affects the size of the tooltip
                        UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_UPDATE, 0, 0);
                    }
                }
            }
        }

        private Control TopLevelControl
        {
            get
            {
                if (_topLevelControl != null)
                {
                    return _topLevelControl;
                }

                Control baseVar = null;
                Control[] regions = new Control[_tools.Keys.Count];
                _tools.Keys.CopyTo(regions, 0);
                for (int i = 0; i < regions.Length; i++)
                {
                    Control ctl = regions[i];
                    baseVar = ctl.TopLevelControlInternal;
                    if (baseVar != null)
                    {
                        break;
                    }

                    if (ctl.IsActiveX)
                    {
                        baseVar = ctl;
                        break;
                    }

                    // In the designer, baseVar can be null since the Parent is not a TopLevel control
                    if (baseVar == null)
                    {
                        if (ctl != null && ctl.ParentInternal != null)
                        {
                            while (ctl.ParentInternal != null)
                            {
                                ctl = ctl.ParentInternal;
                            }

                            baseVar = ctl;
                            if (baseVar != null)
                            {
                                break;
                            }
                        }
                    }
                }

                _topLevelControl = baseVar;
                if (baseVar != null)
                {
                    baseVar.HandleCreated += new EventHandler(TopLevelCreated);
                    baseVar.HandleDestroyed += new EventHandler(TopLevelDestroyed);
                    if (baseVar.IsHandleCreated)
                    {
                        TopLevelCreated(baseVar, EventArgs.Empty);
                    }

                    baseVar.ParentChanged += new EventHandler(OnTopLevelPropertyChanged);
                }

                return baseVar;
            }
        }

        /// <summary>
        /// When set to true, animations are used when tooltip is shown or hidden.
        /// </summary>
        [SRDescription(nameof(SR.ToolTipUseAnimationDescr))]
        [Browsable(true)]
        [DefaultValue(true)]
        public bool UseAnimation
        {
            get => _useAnimation;
            set
            {
                if (_useAnimation != value)
                {
                    _useAnimation = value;
                    if (GetHandleCreated())
                    {
                        RecreateHandle();
                    }
                }
            }
        }

        /// <summary>
        /// When set to true, a fade effect is used when tooltips are shown or hidden.
        /// </summary>
        [SRDescription(nameof(SR.ToolTipUseFadingDescr))]
        [Browsable(true)]
        [DefaultValue(true)]
        public bool UseFading
        {
            get => _useFading;
            set
            {
                if (_useFading != value)
                {
                    _useFading = value;
                    if (GetHandleCreated())
                    {
                        RecreateHandle();
                    }
                }
            }
        }

        /// <summary>
        /// Fires in OwnerDraw mode when the tooltip needs to be drawn.
        /// </summary>
        [SRCategory(nameof(SR.CatBehavior))]
        [SRDescription(nameof(SR.ToolTipDrawEventDescr))]
        public event DrawToolTipEventHandler Draw
        {
            add => _onDraw += value;
            remove => _onDraw -= value;
        }

        /// <summary>
        /// Fires when the tooltip is just about to be shown.
        /// </summary>
        [SRCategory(nameof(SR.CatBehavior))]
        [SRDescription(nameof(SR.ToolTipPopupEventDescr))]
        public event PopupEventHandler Popup
        {
            add => _onPopup += value;
            remove => _onPopup -= value;
        }

        /// <summary>
        /// Adjusts the other delay values based on the Automatic value.
        /// </summary>
        private void AdjustBaseFromAuto()
        {
            _delayTimes[NativeMethods.TTDT_RESHOW] = _delayTimes[NativeMethods.TTDT_AUTOMATIC] / ReshowRatio;
            _delayTimes[NativeMethods.TTDT_AUTOPOP] = _delayTimes[NativeMethods.TTDT_AUTOMATIC] * AutoPopRatio;
            _delayTimes[NativeMethods.TTDT_INITIAL] = _delayTimes[NativeMethods.TTDT_AUTOMATIC];
        }

        private void HandleCreated(object sender, EventArgs eventargs)
        {
            // Reset the toplevel control when the owner's handle is recreated.
            ClearTopLevelControlEvents();
            _topLevelControl = null;

            Control control = (Control)sender;
            CreateRegion(control);
            CheckNativeToolTip(control);
            CheckCompositeControls(control);

            KeyboardToolTipStateMachine.Instance.Hook(control, this);
        }

        private void CheckNativeToolTip(Control associatedControl)
        {
            // Wait for the Handle Creation.
            if (!GetHandleCreated())
            {
                return;
            }

            if (associatedControl is TreeView treeView && treeView.ShowNodeToolTips)
            {
                treeView.SetToolTip(this, GetToolTip(associatedControl));
            }

            if (associatedControl is ToolBar toolBar)
            {
                toolBar.SetToolTip(this);
            }

            if (associatedControl is TabControl tabControl && tabControl.ShowToolTips)
            {
                tabControl.SetToolTip(this, GetToolTip(associatedControl));
            }

            if (associatedControl is ListView listView)
            {
                listView.SetToolTip(this, GetToolTip(associatedControl));
            }

            if (associatedControl is StatusBar statusBar)
            {
                statusBar.SetToolTip(this);
            }

            // Label now has its own Tooltip for AutoEllipsis.
            // So this control too falls in special casing.
            // We need to disable the LABEL AutoEllipsis tooltip and show
            // this tooltip always.
            if (associatedControl is Label label)
            {
                label.SetToolTip(this);
            }
        }

        private void CheckCompositeControls(Control associatedControl)
        {
            if (associatedControl is UpDownBase upDownBase)
            {
                upDownBase.SetToolTip(this, GetToolTip(associatedControl));
            }
        }

        private void HandleDestroyed(object sender, EventArgs eventargs)
        {
            Control control = (Control)sender;
            DestroyRegion(control);

            KeyboardToolTipStateMachine.Instance.Unhook(control, this);
        }

        /// <summary>
        /// Fires the Draw event.
        /// </summary>
        private void OnDraw(DrawToolTipEventArgs e) => _onDraw?.Invoke(this, e);

        /// <summary>
        /// Fires the Popup event.
        /// </summary>
        private void OnPopup(PopupEventArgs e) => _onPopup?.Invoke(this, e);

        private void TopLevelCreated(object sender, EventArgs eventargs)
        {
            CreateHandle();
            CreateAllRegions();
        }

        private void TopLevelDestroyed(object sender, EventArgs eventargs)
        {
            DestoyAllRegions();
            DestroyHandle();
        }

        /// <summary>
        /// Returns true if the tooltip can offer an extender property to the specified target component.
        /// </summary>
        public bool CanExtend(object target) => target is Control;

        private void ClearTopLevelControlEvents()
        {
            if (_topLevelControl != null)
            {
                _topLevelControl.ParentChanged -= new EventHandler(OnTopLevelPropertyChanged);
                _topLevelControl.HandleCreated -= new EventHandler(TopLevelCreated);
                _topLevelControl.HandleDestroyed -= new EventHandler(TopLevelDestroyed);
            }
        }

        /// <summary>
        /// Creates the handle for the control.
        /// </summary>
        private void CreateHandle()
        {
            if (GetHandleCreated())
            {
                return;
            }

            IntPtr userCookie = UnsafeNativeMethods.ThemingScope.Activate();
            try
            {

                var icc = new NativeMethods.INITCOMMONCONTROLSEX
                {
                    dwICC = NativeMethods.ICC_TAB_CLASSES
                };
                SafeNativeMethods.InitCommonControlsEx(icc);

                CreateParams cp = CreateParams; // Avoid reentrant call to CreateHandle
                if (GetHandleCreated())
                {
                    return;
                }

                _window.CreateHandle(cp);
            }
            finally
            {
                UnsafeNativeMethods.ThemingScope.Deactivate(userCookie);
            }

            // If in OwnerDraw mode, we don't want the default border.
            if (OwnerDraw)
            {
                int style = unchecked((int)((long)UnsafeNativeMethods.GetWindowLong(new HandleRef(this, Handle), NativeMethods.GWL_STYLE)));
                style &= ~NativeMethods.WS_BORDER;
                UnsafeNativeMethods.SetWindowLong(new HandleRef(this, Handle), NativeMethods.GWL_STYLE, new HandleRef(null, (IntPtr)style));
            }

            // Setting the max width has the added benefit of enabling multiline tool tips.
            UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETMAXTIPWIDTH, 0, SystemInformation.MaxWindowTrackSize.Width);

            Debug.Assert(NativeMethods.TTDT_AUTOMATIC == 0, "TTDT_AUTOMATIC != 0");

            if (_auto)
            {
                SetDelayTime(NativeMethods.TTDT_AUTOMATIC, _delayTimes[NativeMethods.TTDT_AUTOMATIC]);
                _delayTimes[NativeMethods.TTDT_AUTOPOP] = GetDelayTime(NativeMethods.TTDT_AUTOPOP);
                _delayTimes[NativeMethods.TTDT_INITIAL] = GetDelayTime(NativeMethods.TTDT_INITIAL);
                _delayTimes[NativeMethods.TTDT_RESHOW] = GetDelayTime(NativeMethods.TTDT_RESHOW);
            }
            else
            {
                for (int i = 1; i < _delayTimes.Length; i++)
                {
                    if (_delayTimes[i] >= 1)
                    {
                        SetDelayTime(i, _delayTimes[i]);
                    }
                }
            }

            // Set active status
            UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_ACTIVATE, (active == true) ? 1 : 0, 0);

            if (BackColor != SystemColors.Info)
            {
                UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETTIPBKCOLOR, ColorTranslator.ToWin32(BackColor), 0);
            }
            if (ForeColor != SystemColors.InfoText)
            {
                UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETTIPTEXTCOLOR, ColorTranslator.ToWin32(ForeColor), 0);
            }
            if (_toolTipIcon > 0 || !string.IsNullOrEmpty(_toolTipTitle))
            {
                // If the title is null/empty, the icon won't display.
                string title = !string.IsNullOrEmpty(_toolTipTitle) ? _toolTipTitle : " ";
                UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETTITLE, (int)_toolTipIcon, title);
            }
        }

        private void CreateAllRegions()
        {
            Control[] ctls = new Control[_tools.Keys.Count];
            _tools.Keys.CopyTo(ctls, 0);
            for (int i = 0; i < ctls.Length; i++)
            {
                // DataGridView manages its own tool tip.
                if (ctls[i] is DataGridView)
                {
                    return;
                }

                CreateRegion(ctls[i]);
            }
        }

        private void DestoyAllRegions()
        {
            Control[] ctls = new Control[_tools.Keys.Count];
            _tools.Keys.CopyTo(ctls, 0);
            for (int i = 0; i < ctls.Length; i++)
            {
                // DataGridView manages its own tool tip.
                if (ctls[i] is DataGridView)
                {
                    return;
                }

                DestroyRegion(ctls[i]);
            }
        }

        private void SetToolInfo(Control ctl, string caption)
        {
            NativeMethods.TOOLINFO_TOOLTIP tool = GetTOOLINFO(ctl, caption, out bool allocatedString);
            try
            {
                int ret = (int)UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_ADDTOOL, 0, tool);
                if (ctl is TreeView tv && tv.ShowNodeToolTips)
                {
                    return;
                }
                else if (ctl is ListView lv && lv.ShowItemToolTips)
                {
                    return;
                }

                if (ret == 0)
                {
                    throw new InvalidOperationException(SR.ToolTipAddFailed);
                }
            }
            finally
            {
                if (allocatedString && IntPtr.Zero != tool.lpszText)
                {
                    Marshal.FreeHGlobal(tool.lpszText);
                }
            }
        }

        private void CreateRegion(Control ctl)
        {
            string caption = GetToolTip(ctl);
            bool handlesCreated = ctl.IsHandleCreated
                                  && TopLevelControl != null
                                  && TopLevelControl.IsHandleCreated;
            if (!_created.ContainsKey(ctl) && !string.IsNullOrEmpty(caption)
                && handlesCreated && !DesignMode)
            {
                // Call the SendMessage through a function.
                SetToolInfo(ctl, caption);
                _created[ctl] = ctl;
            }
            if (ctl.IsHandleCreated && _topLevelControl == null)
            {
                // Remove first to purge any duplicates.
                ctl.MouseMove -= new MouseEventHandler(MouseMove);
                ctl.MouseMove += new MouseEventHandler(MouseMove);
            }
        }

        private void MouseMove(object sender, MouseEventArgs me)
        {
            Control ctl = (Control)sender;
            if (!_created.ContainsKey(ctl) && ctl.IsHandleCreated && TopLevelControl != null)
            {

                CreateRegion(ctl);
            }

            if (_created.ContainsKey(ctl))
            {
                ctl.MouseMove -= new MouseEventHandler(MouseMove);
            }
        }

        /// <summary>
        /// Destroys the handle for this control.
        /// Required by Label to destroy the handle for the toolTip added for AutoEllipses.
        /// </summary>
        internal void DestroyHandle()
        {
            if (GetHandleCreated())
            {
                _window.DestroyHandle();
            }
        }

        private void DestroyRegion(Control ctl)
        {
            // When the toplevelControl is a form and is Modal, the Handle of the tooltip is released
            // before we come here. In such a case the tool wont get deleted from the tooltip.
            // So we dont check "Handle" in the handlesCreate but check it only foe Non-Nodal dialogs later
            bool handlesCreated = ctl.IsHandleCreated
                                && _topLevelControl != null
                                && _topLevelControl.IsHandleCreated
                                && !_isDisposing;
            if (!(_topLevelControl is Form topForm) || (topForm != null && !topForm.Modal))
            {
                handlesCreated = handlesCreated && GetHandleCreated();
            }

            if (_created.ContainsKey(ctl) && handlesCreated && !DesignMode)
            {

                UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_DELTOOL, 0, GetMinTOOLINFO(ctl));
                _created.Remove(ctl);
            }
        }

        /// <summary>
        /// Disposes of the <see cref="ToolTip"/> component.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _isDisposing = true;
                try
                {
                    ClearTopLevelControlEvents();
                    StopTimer();

                    // Always destroy the handle.
                    DestroyHandle();
                    RemoveAll();

                    _window = null;

                    // Unhook the DeactiveEvent. Find the Form for associated Control and hook
                    // up to the Deactivated event to Hide the Shown tooltip
                    if (TopLevelControl is Form baseFrom)
                    {
                        baseFrom.Deactivate -= new EventHandler(BaseFormDeactivate);
                    }
                }
                finally
                {
                    _isDisposing = false;
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Returns the delayTime based on the NativeMethods.TTDT_* values.
        /// </summary>
        internal int GetDelayTime(int type)
        {
            if (!GetHandleCreated())
            {
                return _delayTimes[type];
            }

            return (int)UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_GETDELAYTIME, type, 0);
        }

        internal bool GetHandleCreated() => _window != null && _window.Handle != IntPtr.Zero;

        /// <summary>
        /// Returns a new instance of the TOOLINFO_T structure with the minimum required data to
        /// uniquely identify a region. This is used primarily for delete operations.
        /// NOTE: This cannot force the creation of a handle.
        /// </summary>
        private NativeMethods.TOOLINFO_TOOLTIP GetMinTOOLINFO(Control ctl)
        {
            return GetMinToolInfoForHandle(ctl.Handle);
        }

        private NativeMethods.TOOLINFO_TOOLTIP GetMinToolInfoForTool(IWin32Window tool)
        {
            return GetMinToolInfoForHandle(tool.Handle);
        }

        private NativeMethods.TOOLINFO_TOOLTIP GetMinToolInfoForHandle(IntPtr handle)
        {
            var ti = new NativeMethods.TOOLINFO_TOOLTIP
            {
                cbSize = Marshal.SizeOf<NativeMethods.TOOLINFO_TOOLTIP>(),
                hwnd = handle
            };
            ti.uFlags |= NativeMethods.TTF_IDISHWND;
            ti.uId = handle;
            return ti;
        }

        /// <summary>
        /// Returns a detailed TOOLINFO_TOOLTIP structure that represents the specified region.
        /// NOTE: This may force the creation of a handle.
        /// If the out parameter allocatedString has been set to true, It is the responsibility of
        /// the caller to free the string buffer referenced by lpszText (using Marshal.FreeHGlobal).
        /// </summary>
        private NativeMethods.TOOLINFO_TOOLTIP GetTOOLINFO(Control ctl, string caption, out bool allocatedString)
        {
            allocatedString = false;
            NativeMethods.TOOLINFO_TOOLTIP ti = GetMinTOOLINFO(ctl);
            ti.cbSize = Marshal.SizeOf<NativeMethods.TOOLINFO_TOOLTIP>();
            ti.uFlags |= NativeMethods.TTF_TRANSPARENT | NativeMethods.TTF_SUBCLASS;

            // RightToLeft reading order
            Control richParent = TopLevelControl;
            if (richParent != null && richParent.RightToLeft == RightToLeft.Yes && !ctl.IsMirrored)
            {
                // Indicates that the ToolTip text will be displayed in the opposite direction
                // to the text in the parent window.
                ti.uFlags |= NativeMethods.TTF_RTLREADING;
            }

            if (ctl is TreeView || ctl is ListView)
            {
                if (ctl is TreeView tv && tv.ShowNodeToolTips)
                {
                    ti.lpszText = NativeMethods.InvalidIntPtr;
                }
                else if (ctl is ListView lv && lv.ShowItemToolTips)
                {
                    ti.lpszText = NativeMethods.InvalidIntPtr;
                }
                else
                {
                    ti.lpszText = Marshal.StringToHGlobalAuto(caption);
                    allocatedString = true;
                }
            }
            else
            {
                ti.lpszText = Marshal.StringToHGlobalAuto(caption);
                allocatedString = true;
            }

            return ti;
        }

        private NativeMethods.TOOLINFO_TOOLTIP GetWinTOOLINFO(IntPtr hWnd)
        {
            var ti = new NativeMethods.TOOLINFO_TOOLTIP
            {
                cbSize = Marshal.SizeOf<NativeMethods.TOOLINFO_TOOLTIP>(),
                hwnd = hWnd
            };
            ti.uFlags |= NativeMethods.TTF_IDISHWND | NativeMethods.TTF_TRANSPARENT | NativeMethods.TTF_SUBCLASS;

            // RightToLeft reading order
            Control richParent = TopLevelControl;
            if (richParent != null && richParent.RightToLeft == RightToLeft.Yes)
            {
                bool isWindowMirrored = ((unchecked((int)(long)UnsafeNativeMethods.GetWindowLong(new HandleRef(this, hWnd), NativeMethods.GWL_STYLE)) & NativeMethods.WS_EX_LAYOUTRTL) == NativeMethods.WS_EX_LAYOUTRTL);
                // Indicates that the ToolTip text will be displayed in the opposite direction
                // to the text in the parent window.
                if (!isWindowMirrored)
                {
                    ti.uFlags |= NativeMethods.TTF_RTLREADING;
                }
            }

            ti.uId = ti.hwnd;
            return ti;
        }

        /// <summary>
        /// Retrieves the <see cref="ToolTip"/> text associated with the specified control.
        /// </summary>
        [DefaultValue(""),
        Localizable(true)]
        [Description(nameof(SR.ToolTipToolTipDescr)),
        Editor("System.ComponentModel.Design.MultilineStringEditor, " + AssemblyRef.SystemDesign, typeof(Drawing.Design.UITypeEditor))]
        public string GetToolTip(Control control)
        {
            if (control == null)
            {
                return string.Empty;
            }

            TipInfo tt = (TipInfo)_tools[control];
            return tt?.Caption ?? string.Empty;
        }

        /// <summary>
        /// Returns the HWND of the window that is at the specified point. This handles special
        /// cases where one Control owns multiple HWNDs (i.e. ComboBox).
        /// </summary>
        private IntPtr GetWindowFromPoint(Point screenCoords, ref bool success)
        {
            Control baseVar = TopLevelControl;

            // Special case ActiveX Controls.
            if (baseVar != null && baseVar.IsActiveX)
            {
                // Find the matching HWnd matching the ScreenCoord and find if the Control has a Tooltip.
                IntPtr hwndControl = UnsafeNativeMethods.WindowFromPoint(screenCoords.X, screenCoords.Y);
                if (hwndControl != IntPtr.Zero)
                {
                    Control currentControl = Control.FromHandle(hwndControl);
                    if (currentControl != null && _tools != null && _tools.ContainsKey(currentControl))
                    {
                        return hwndControl;
                    }
                }
                return IntPtr.Zero;
            }

            IntPtr baseHwnd = baseVar?.Handle ?? IntPtr.Zero;
            IntPtr hwnd = IntPtr.Zero;
            bool finalMatch = false;
            while (!finalMatch)
            {
                Point pt = screenCoords;
                if (baseVar != null)
                {
                    pt = baseVar.PointToClient(screenCoords);
                }

                IntPtr found = UnsafeNativeMethods.ChildWindowFromPointEx(new HandleRef(null, baseHwnd), pt.X, pt.Y, NativeMethods.CWP_SKIPINVISIBLE);
                if (found == baseHwnd)
                {
                    hwnd = found;
                    finalMatch = true;
                }
                else if (found == IntPtr.Zero)
                {
                    finalMatch = true;
                }
                else
                {
                    baseVar = Control.FromHandle(found);
                    if (baseVar == null)
                    {
                        baseVar = Control.FromChildHandle(found);
                        if (baseVar != null)
                        {
                            hwnd = baseVar.Handle;
                        }

                        finalMatch = true;
                    }
                    else
                    {
                        baseHwnd = baseVar.Handle;
                    }
                }
            }

            if (hwnd != IntPtr.Zero)
            {
                Control ctl = Control.FromHandle(hwnd);
                if (ctl != null)
                {
                    Control current = ctl;
                    while (current != null && current.Visible)
                    {
                        current = current.ParentInternal;
                    }
                    if (current != null)
                    {
                        hwnd = IntPtr.Zero;
                    }

                    success = true;
                }
            }

            return hwnd;
        }

        private void OnTopLevelPropertyChanged(object s, EventArgs e)
        {
            ClearTopLevelControlEvents();
            _topLevelControl = null;

            // We must re-aquire this control.  If the existing top level control's handle
            // was never created, but the new parent has a handle, if we don't re-get
            // the top level control here we won't ever create the tooltip handle.
            _topLevelControl = TopLevelControl;
        }

        private void RecreateHandle()
        {
            if (!DesignMode)
            {
                if (GetHandleCreated())
                {
                    DestroyHandle();
                }

                _created.Clear();
                CreateHandle();
                CreateAllRegions();
            }
        }

        /// <summary>
        /// Removes all of the tooltips currently associated with the <see cref="ToolTip"/> control.
        /// </summary>
        public void RemoveAll()
        {
            Control[] regions = new Control[_tools.Keys.Count];
            _tools.Keys.CopyTo(regions, 0);
            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i].IsHandleCreated)
                {
                    DestroyRegion(regions[i]);
                }

                regions[i].HandleCreated -= new EventHandler(HandleCreated);
                regions[i].HandleDestroyed -= new EventHandler(HandleDestroyed);

                KeyboardToolTipStateMachine.Instance.Unhook(regions[i], this);
            }

            _created.Clear();
            _tools.Clear();

            ClearTopLevelControlEvents();
            _topLevelControl = null;

            KeyboardToolTipStateMachine.Instance.ResetStateMachine(this);
        }

        /// <summary>
        /// Sets the delayTime based on the NativeMethods.TTDT_* values.
        /// </summary>
        private void SetDelayTime(int type, int time)
        {
            _auto = type == NativeMethods.TTDT_AUTOMATIC;
            _delayTimes[type] = time;

            if (GetHandleCreated() && time >= 0)
            {
                UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETDELAYTIME, type, time);

                // Update everyone else if automatic is set. we need to do this
                // to preserve value in case of handle recreation.
                if (_auto)
                {
                    _delayTimes[NativeMethods.TTDT_AUTOPOP] = GetDelayTime(NativeMethods.TTDT_AUTOPOP);
                    _delayTimes[NativeMethods.TTDT_INITIAL] = GetDelayTime(NativeMethods.TTDT_INITIAL);
                    _delayTimes[NativeMethods.TTDT_RESHOW] = GetDelayTime(NativeMethods.TTDT_RESHOW);
                }
            }
            else if (_auto)
            {
                AdjustBaseFromAuto();
            }
        }

        /// <summary>
        /// Associates <see cref="ToolTip"/> text with the specified control.
        /// </summary>
        public void SetToolTip(Control control, string caption)
        {
            TipInfo info = new TipInfo(caption, TipInfo.Type.Auto);
            SetToolTipInternal(control, info);
        }

        /// <summary>
        /// Associates <see cref="ToolTip"/> text with the specified information
        /// </summary>
        private void SetToolTipInternal(Control control, TipInfo info)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            bool exists = _tools.ContainsKey(control);
            bool empty = info == null || string.IsNullOrEmpty(info.Caption);
            if (exists && empty)
            {
                _tools.Remove(control);
            }
            else if (!empty)
            {
                _tools[control] = info;
            }

            if (!empty && !exists)
            {
                control.HandleCreated += new EventHandler(HandleCreated);
                control.HandleDestroyed += new EventHandler(HandleDestroyed);

                if (control.IsHandleCreated)
                {
                    HandleCreated(control, EventArgs.Empty);
                }
            }
            else
            {
                bool handlesCreated = control.IsHandleCreated
                                      && TopLevelControl != null
                                      && TopLevelControl.IsHandleCreated;
                if (exists && !empty && handlesCreated && !DesignMode)
                {
                    NativeMethods.TOOLINFO_TOOLTIP toolInfo = GetTOOLINFO(control, info.Caption, out bool allocatedString);
                    try
                    {
                        UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETTOOLINFO,
                                                        0, toolInfo);
                    }
                    finally
                    {
                        if (allocatedString && IntPtr.Zero != toolInfo.lpszText)
                        {
                            Marshal.FreeHGlobal(toolInfo.lpszText);
                        }
                    }

                    CheckNativeToolTip(control);
                    CheckCompositeControls(control);
                }
                else if (empty && exists && !DesignMode)
                {
                    control.HandleCreated -= new EventHandler(HandleCreated);
                    control.HandleDestroyed -= new EventHandler(HandleDestroyed);

                    if (control.IsHandleCreated)
                    {
                        HandleDestroyed(control, EventArgs.Empty);
                    }

                    _created.Remove(control);
                }
            }
        }

        /// <summary>
        /// Returns true if the AutomaticDelay property should be persisted.
        /// </summary>
        private bool ShouldSerializeAutomaticDelay() => _auto && AutomaticDelay != DefaultDelay;

        /// <summary>
        /// Returns true if the AutoPopDelay property should be persisted.
        /// </summary>
        private bool ShouldSerializeAutoPopDelay() => !_auto;

        /// <summary>
        /// Returns true if the InitialDelay property should be persisted.
        /// </summary>
        private bool ShouldSerializeInitialDelay() => !_auto;

        /// <summary>
        /// Returns true if the ReshowDelay property should be persisted.
        /// </summary>
        private bool ShouldSerializeReshowDelay() => !_auto;

        /// <summary>
        /// Shows a tooltip for specified text, window, and hotspot
        /// </summary>
        private void ShowTooltip(string text, IWin32Window window, int duration)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (window is Control associatedControl)
            {
                var r = new NativeMethods.RECT();
                UnsafeNativeMethods.GetWindowRect(new HandleRef(associatedControl, associatedControl.Handle), ref r);

                Cursor currentCursor = Cursor.CurrentInternal;
                Point cursorLocation = Cursor.Position;
                Point p = cursorLocation;

                Screen screen = Screen.FromPoint(cursorLocation);

                // Place the tool tip on the associated control if its not already there
                if (cursorLocation.X < r.left || cursorLocation.X > r.right ||
                     cursorLocation.Y < r.top || cursorLocation.Y > r.bottom)
                {
                    // Calculate the dimensions of the visible rectangle which
                    // is used to estimate the upper x,y of the tooltip placement
                    NativeMethods.RECT visibleRect = new NativeMethods.RECT
                    {
                        left = (r.left < screen.WorkingArea.Left) ? screen.WorkingArea.Left : r.left,
                        top = (r.top < screen.WorkingArea.Top) ? screen.WorkingArea.Top : r.top,
                        right = (r.right > screen.WorkingArea.Right) ? screen.WorkingArea.Right : r.right,
                        bottom = (r.bottom > screen.WorkingArea.Bottom) ? screen.WorkingArea.Bottom : r.bottom
                    };

                    p.X = visibleRect.left + (visibleRect.right - visibleRect.left) / 2;
                    p.Y = visibleRect.top + (visibleRect.bottom - visibleRect.top) / 2;
                    associatedControl.PointToClient(p);
                    SetTrackPosition(p.X, p.Y);
                    SetTool(window, text, TipInfo.Type.SemiAbsolute, p);

                    if (duration > 0)
                    {
                        StartTimer(this._window, duration);
                    }
                }
                else
                {
                    TipInfo tt = (TipInfo)_tools[associatedControl];
                    if (tt == null)
                    {
                        tt = new TipInfo(text, TipInfo.Type.SemiAbsolute);
                    }
                    else
                    {
                        tt.TipType |= TipInfo.Type.SemiAbsolute;
                        tt.Caption = text;
                    }

                    tt.Position = p;
                    if (duration > 0)
                    {
                        if (_originalPopupDelay == 0)
                        {
                            _originalPopupDelay = AutoPopDelay;
                        }

                        AutoPopDelay = duration;
                    }

                    SetToolTipInternal(associatedControl, tt);
                }
            }
        }

        /// <summary>
        /// Associates <see cref="ToolTip"/> with the specified control and displays it.
        /// </summary>
        public void Show(string text, IWin32Window window)
        {
            // Check if the foreground window is the TopLevelWindow
            if (IsWindowActive(window))
            {
                ShowTooltip(text, window, 0);
            }
        }

        /// <summary>
        /// Associates <see cref="ToolTip"/> with the specified control and displays it for the
        /// specified duration.
        /// </summary>
        public void Show(string text, IWin32Window window, int duration)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }
            if (duration < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), duration, string.Format(SR.InvalidLowBoundArgumentEx, nameof(duration), duration, 0));
            }

            if (IsWindowActive(window))
            {
                ShowTooltip(text, window, duration);
            }
        }

        /// <summary>
        /// Associates <see cref="ToolTip"/> with the specified control and displays it.
        /// </summary>
        public void Show(string text, IWin32Window window, Point point)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (IsWindowActive(window))
            {
                // Set the ToolTips.
                var r = new NativeMethods.RECT();
                UnsafeNativeMethods.GetWindowRect(new HandleRef(window, Control.GetSafeHandle(window)), ref r);
                int pointX = r.left + point.X;
                int pointY = r.top + point.Y;

                SetTrackPosition(pointX, pointY);
                SetTool(window, text, TipInfo.Type.Absolute, new Point(pointX, pointY));
            }
        }

        /// <summary>
        /// Associates <see cref="ToolTip"/> with the specified control and displays it.
        /// </summary>
        public void Show(string text, IWin32Window window, Point point, int duration)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }
            if (duration < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), duration, string.Format(SR.InvalidLowBoundArgumentEx, nameof(duration), duration, 0));
            }

            if (IsWindowActive(window))
            {
                // Set the ToolTips.
                var r = new NativeMethods.RECT();
                UnsafeNativeMethods.GetWindowRect(new HandleRef(window, Control.GetSafeHandle(window)), ref r);
                int pointX = r.left + point.X;
                int pointY = r.top + point.Y;
                SetTrackPosition(pointX, pointY);
                SetTool(window, text, TipInfo.Type.Absolute, new Point(pointX, pointY));
                StartTimer(window, duration);
            }
        }

        /// <summary>
        /// Associates <see cref="ToolTip"/> with the specified control and displays it.
        /// </summary>
        public void Show(string text, IWin32Window window, int x, int y)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (IsWindowActive(window))
            {
                var r = new NativeMethods.RECT();
                UnsafeNativeMethods.GetWindowRect(new HandleRef(window, Control.GetSafeHandle(window)), ref r);
                int pointX = r.left + x;
                int pointY = r.top + y;
                SetTrackPosition(pointX, pointY);
                SetTool(window, text, TipInfo.Type.Absolute, new Point(pointX, pointY));
            }
        }

        /// <summary>
        /// Associates <see cref="ToolTip"/> with the specified control and displays it.
        /// </summary>
        public void Show(string text, IWin32Window window, int x, int y, int duration)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }
            if (duration < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), duration, string.Format(SR.InvalidLowBoundArgumentEx, nameof(duration), duration, 0));
            }

            if (IsWindowActive(window))
            {
                var r = new NativeMethods.RECT();
                UnsafeNativeMethods.GetWindowRect(new HandleRef(window, Control.GetSafeHandle(window)), ref r);
                int pointX = r.left + x;
                int pointY = r.top + y;
                SetTrackPosition(pointX, pointY);
                SetTool(window, text, TipInfo.Type.Absolute, new Point(pointX, pointY));
                StartTimer(window, duration);
            }
        }

        internal void ShowKeyboardToolTip(string text, IKeyboardToolTip tool, int duration)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }
            if (duration < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), string.Format(SR.InvalidLowBoundArgumentEx, nameof(duration), (duration).ToString(CultureInfo.CurrentCulture), 0));
            }

            Rectangle toolRectangle = tool.GetNativeScreenRectangle();
            // At first, place the tooltip at the middle of the tool (default location)
            int pointX = (toolRectangle.Left + toolRectangle.Right) / 2;
            int pointY = (toolRectangle.Top + toolRectangle.Bottom) / 2;
            SetTool(tool.GetOwnerWindow(), text, TipInfo.Type.Absolute, new Point(pointX, pointY));

            // Then look for a better ToolTip location
            if (TryGetBubbleSize(tool, toolRectangle, out Size bubbleSize))
            {
                Point optimalPoint = GetOptimalToolTipPosition(tool, toolRectangle, bubbleSize.Width, bubbleSize.Height);

                // The optimal point should be used as a tracking position
                pointX = optimalPoint.X;
                pointY = optimalPoint.Y;

                // Update TipInfo for the tool with optimal position
                TipInfo tipInfo = (TipInfo)(_tools[tool] ?? _tools[tool.GetOwnerWindow()]);
                tipInfo.Position = new Point(pointX, pointY);

                // Ensure that the tooltip bubble is moved to the optimal position even when a mouse tooltip is being replaced with a keyboard tooltip
                Reposition(optimalPoint, bubbleSize);
            }

            SetTrackPosition(pointX, pointY);
            StartTimer(tool.GetOwnerWindow(), duration);
        }

        private bool TryGetBubbleSize(IKeyboardToolTip tool, Rectangle toolRectangle, out Size bubbleSize)
        {
            // Get bubble size to use it for optimal position calculation
            IntPtr bubbleSizeInt = UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_GETBUBBLESIZE, 0, GetMinToolInfoForTool(tool.GetOwnerWindow()));
            if (bubbleSizeInt.ToInt32() == NativeMethods.S_FALSE)
            {
                bubbleSize = Size.Empty;
                return false;
            }

            int width = NativeMethods.Util.LOWORD(bubbleSizeInt);
            int height = NativeMethods.Util.HIWORD(bubbleSizeInt);
            bubbleSize = new Size(width, height);
            return true;
        }

        private Point GetOptimalToolTipPosition(IKeyboardToolTip tool, Rectangle toolRectangle, int width, int height)
        {
            // Possible tooltip locations are tied to the tool rectangle bounds
            int centeredX = toolRectangle.Left + toolRectangle.Width / 2 - width / 2; // tooltip will be aligned with tool vertically
            int centeredY = toolRectangle.Top + toolRectangle.Height / 2 - height / 2; // tooltip will be aligned with tool horizontally

            Rectangle[] possibleLocations = new Rectangle[LocationTotal];
            possibleLocations[LocationIndexTop] = new Rectangle(centeredX, toolRectangle.Top - height, width, height);
            possibleLocations[LocationIndexRight] = new Rectangle(toolRectangle.Right, centeredY, width, height);
            possibleLocations[LocationIndexBottom] = new Rectangle(centeredX, toolRectangle.Bottom, width, height);
            possibleLocations[LocationIndexLeft] = new Rectangle(toolRectangle.Left - width, centeredY, width, height);

            // Neighboring tools should not be overlapped (ideally) by tooltip
            IList<Rectangle> neighboringToolsRectangles = tool.GetNeighboringToolsRectangles();

            // Weights are used to determine which one of the possible location overlaps least area of the neighboring tools
            long[] locationWeights = new long[LocationTotal];

            // Check if the possible locations intersect with the neighboring tools
            for (int i = 0; i < possibleLocations.Length; i++)
            {
                foreach (Rectangle neighboringToolRectangle in neighboringToolsRectangles)
                {
                    Rectangle intersection = Rectangle.Intersect(possibleLocations[i], neighboringToolRectangle);
                    checked
                    {
                        locationWeights[i] += Math.Abs((long)intersection.Width * intersection.Height); // Intersection is a weight
                    }
                }
            }

            // Calculate clipped area of possible locations i.e. area which is located outside the screen area
            Rectangle screenBounds = SystemInformation.VirtualScreen;
            long[] locationClippedAreas = new long[LocationTotal];
            for (int i = 0; i < possibleLocations.Length; i++)
            {
                Rectangle locationAreaWithinScreen = Rectangle.Intersect(screenBounds, possibleLocations[i]);
                checked
                {
                    locationClippedAreas[i] = (Math.Abs((long)possibleLocations[i].Width) - Math.Abs((long)locationAreaWithinScreen.Width))
                        * (Math.Abs((long)possibleLocations[i].Height) - Math.Abs((long)locationAreaWithinScreen.Height));
                }
            }

            // Calculate area of possible locations within top level control rectangle
            long[] locationWithinTopControlAreas = new long[LocationTotal];
            Rectangle topContainerBounds = ((IKeyboardToolTip)TopLevelControl)?.GetNativeScreenRectangle() ?? Rectangle.Empty;
            if (!topContainerBounds.IsEmpty)
            {
                for (int i = 0; i < possibleLocations.Length; i++)
                {
                    Rectangle locationWithinTopControlRectangle = Rectangle.Intersect(topContainerBounds, possibleLocations[i]);
                    checked
                    {
                        locationWithinTopControlAreas[i] = Math.Abs((long)locationWithinTopControlRectangle.Height * locationWithinTopControlRectangle.Width);
                    }
                }
            }

            // Pick optimal location
            long leastWeight = locationWeights[0];
            long leastClippedArea = locationClippedAreas[0];
            long biggestAreaWithinTopControl = locationWithinTopControlAreas[0];
            int locationIndex = 0;
            Rectangle optimalLocation = possibleLocations[0];
            bool rtlEnabled = tool.HasRtlModeEnabled();
            for (int i = 1; i < possibleLocations.Length; i++)
            {
                if (IsCompetingLocationBetter(leastClippedArea, leastWeight, biggestAreaWithinTopControl, locationIndex,
                    locationClippedAreas[i], locationWeights[i], locationWithinTopControlAreas[i], i,
                    rtlEnabled))
                {
                    leastWeight = locationWeights[i];
                    leastClippedArea = locationClippedAreas[i];
                    biggestAreaWithinTopControl = locationWithinTopControlAreas[i];
                    locationIndex = i;
                    optimalLocation = possibleLocations[i];
                }
            }

            return new Point(optimalLocation.Left, optimalLocation.Top);
        }

        private bool IsCompetingLocationBetter(long originalLocationClippedArea,
            long originalLocationWeight,
            long originalLocationAreaWithinTopControl,
            int originalIndex,
            long competingLocationClippedArea,
            long competingLocationWeight,
            long competingLocationAreaWithinTopControl,
            int competingIndex,
            bool rtlEnabled)
        {
            if (competingLocationClippedArea < originalLocationClippedArea)
            {
                // Prefer location with less clipped area
                return true;
            }
            else if (competingLocationWeight < originalLocationWeight)
            {
                // Otherwise prefer location with less weight
                return true;
            }
            else if (competingLocationWeight == originalLocationWeight && competingLocationClippedArea == originalLocationClippedArea)
            {
                // Prefer locations located within top level control
                if (competingLocationAreaWithinTopControl > originalLocationAreaWithinTopControl)
                {
                    return true;
                }
                else if (competingLocationAreaWithinTopControl == originalLocationAreaWithinTopControl)
                {
                    switch (originalIndex)
                    {
                        case LocationIndexTop:
                            // Top location is the least preferred location
                            return true;
                        case LocationIndexBottom:
                            // Right and Left locations are preferred instead of Bottom location
                            if (competingIndex == LocationIndexLeft || competingIndex == LocationIndexRight)
                            {
                                return true;
                            }
                            break;
                        case LocationIndexRight:
                            // When RTL is enabled Left location is preferred
                            if (rtlEnabled && competingIndex == LocationIndexLeft)
                            {
                                return true;
                            }
                            break;
                        case LocationIndexLeft:
                            // When RTL is disabled Right location is preferred
                            if (!rtlEnabled && competingIndex == LocationIndexRight)
                            {
                                return true;
                            }
                            break;
                        default:
                            throw new NotSupportedException("Unsupported location index value");
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Private Function to encapsulate TTM_TRACKPOSITION so that this doesnt fire an extra POP event
        /// </summary>
        private void SetTrackPosition(int pointX, int pointY)
        {
            try
            {
                _trackPosition = true;
                UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_TRACKPOSITION, 0, NativeMethods.Util.MAKELONG(pointX, pointY));
            }
            finally
            {
                _trackPosition = false;
            }
        }

        /// <summary>
        /// Hides <see cref="ToolTip"/> with the specified control.
        /// </summary>
        public void Hide(IWin32Window win)
        {
            if (win == null)
            {
                throw new ArgumentNullException(nameof(win));
            }

            if (_window == null)
            {
                return;
            }

            if (GetHandleCreated())
            {
                IntPtr hWnd = Control.GetSafeHandle(win);
                UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_TRACKACTIVATE, 0, GetWinTOOLINFO(hWnd));
                UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_DELTOOL, 0, GetWinTOOLINFO(hWnd));
            }
            StopTimer();

            // Check if the passed in IWin32Window is a Control.
            if (!(win is Control tool))
            {
                _owners.Remove(win.Handle);
            }
            else
            {
                if (_tools.ContainsKey(tool))
                {
                    SetToolInfo(tool, GetToolTip(tool));
                }
                else
                {
                    _owners.Remove(win.Handle);
                }

                // Find the Form for associated Control and hook up to the Deactivated event to
                /// hide the shown tooltip
                Form baseFrom = tool.FindForm();
                if (baseFrom != null)
                {
                    baseFrom.Deactivate -= new EventHandler(BaseFormDeactivate);
                }
            }

            // Clear off the toplevel control.
            ClearTopLevelControlEvents();
            _topLevelControl = null;
        }

        private void BaseFormDeactivate(object sender, EventArgs e)
        {
            HideAllToolTips();
            KeyboardToolTipStateMachine.Instance.NotifyAboutFormDeactivation(this);
        }

        private void HideAllToolTips()
        {
            Control[] ctls = new Control[_owners.Values.Count];
            _owners.Values.CopyTo(ctls, 0);
            for (int i = 0; i < ctls.Length; i++)
            {
                Hide(ctls[i]);
            }
        }

        private void SetTool(IWin32Window win, string text, TipInfo.Type type, Point position)
        {
            Control tool = win as Control;
            if (tool != null && _tools.ContainsKey(tool))
            {
                bool allocatedString = false;
                var ti = new NativeMethods.TOOLINFO_TOOLTIP();
                try
                {
                    ti.cbSize = Marshal.SizeOf<NativeMethods.TOOLINFO_TOOLTIP>();
                    ti.hwnd = tool.Handle;
                    ti.uId = tool.Handle;
                    int ret = (int)UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_GETTOOLINFO, 0, ti);
                    if (ret != 0)
                    {
                        ti.uFlags |= NativeMethods.TTF_TRACK;

                        if (type == TipInfo.Type.Absolute || type == TipInfo.Type.SemiAbsolute)
                        {
                            ti.uFlags |= NativeMethods.TTF_ABSOLUTE;
                        }
                        ti.lpszText = Marshal.StringToHGlobalAuto(text);
                        allocatedString = true;
                    }

                    TipInfo tt = (TipInfo)_tools[tool];
                    if (tt == null)
                    {
                        tt = new TipInfo(text, type);
                    }
                    else
                    {
                        tt.TipType |= type;
                        tt.Caption = text;
                    }
                    tt.Position = position;
                    _tools[tool] = tt;

                    UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETTOOLINFO, 0, ti);
                    UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_TRACKACTIVATE, 1, ti);
                }
                finally
                {
                    if (allocatedString && IntPtr.Zero != ti.lpszText)
                    {
                        Marshal.FreeHGlobal(ti.lpszText);
                    }
                }
            }
            else
            {
                Hide(win);

                // Need to do this BEFORE we call GetWinTOOLINFO, since it relies on the tools array to be populated
                // in order to find the toplevelparent.
                TipInfo tt = (TipInfo)_tools[tool];
                if (tt == null)
                {
                    tt = new TipInfo(text, type);
                }
                else
                {
                    tt.TipType |= type;
                    tt.Caption = text;
                }

                tt.Position = position;
                _tools[tool] = tt;

                IntPtr hWnd = Control.GetSafeHandle(win);
                _owners[hWnd] = win;
                NativeMethods.TOOLINFO_TOOLTIP toolInfo = GetWinTOOLINFO(hWnd);
                toolInfo.uFlags |= NativeMethods.TTF_TRACK;

                if (type == TipInfo.Type.Absolute || type == TipInfo.Type.SemiAbsolute)
                {
                    toolInfo.uFlags |= NativeMethods.TTF_ABSOLUTE;
                }

                try
                {
                    toolInfo.lpszText = Marshal.StringToHGlobalAuto(text);
                    UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_ADDTOOL, 0, toolInfo);
                    UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_TRACKACTIVATE, 1, toolInfo);
                }
                finally
                {
                    if (IntPtr.Zero != toolInfo.lpszText)
                    {
                        Marshal.FreeHGlobal(toolInfo.lpszText);
                    }
                }
            }

            if (tool != null)
            {

                // Lets find the Form for associated Control .
                // and hook up to the Deactivated event to Hide the Shown tooltip
                Form baseFrom = tool.FindForm();
                if (baseFrom != null)
                {
                    baseFrom.Deactivate += new EventHandler(BaseFormDeactivate);
                }
            }
        }

        /// <summary>
        /// Starts the timer hiding Positioned ToolTips
        /// </summary>
        private void StartTimer(IWin32Window owner, int interval)
        {
            if (_timer == null)
            {
                _timer = new ToolTipTimer(owner);
                // Add the timer handler
                _timer.Tick += new EventHandler(TimerHandler);
            }

            _timer.Interval = interval;
            _timer.Start();
        }

        /// <summary>
        /// Stops the timer for hiding Positioned ToolTips
        /// </summary>
        protected void StopTimer()
        {
            // Hold a local ref to timer so that a posted message doesn't null this out during
            // disposal.
            ToolTipTimer timerRef = _timer;

            if (timerRef != null)
            {
                timerRef.Stop();
                timerRef.Dispose();
                _timer = null;
            }
        }

        /// <summary>
        /// Generates updown events when the timer calls this function.
        /// </summary>
        private void TimerHandler(object source, EventArgs args)
        {
            Hide(((ToolTipTimer)source).Host);
        }

        /// <summary>
        /// Finalizes garbage collection.
        /// </summary>
        ~ToolTip() => DestroyHandle();

        /// <summary>
        /// Returns a string representation for this control.
        /// </summary>
        public override string ToString()
        {
            string s = base.ToString();
            return $"{s}InitialDelay: {InitialDelay}, ShowAlways: {ShowAlways}";
        }

        private void Reposition(Point tipPosition, Size tipSize)
        {
            Point moveToLocation = tipPosition;
            Screen screen = Screen.FromPoint(moveToLocation);

            // Re-adjust the X position of the tool tip if it bleeds off the screen working area
            if (moveToLocation.X + tipSize.Width > screen.WorkingArea.Right)
            {
                moveToLocation.X = screen.WorkingArea.Right - tipSize.Width;
            }

            // re-adjust the Y position of the tool tip if it bleeds off the screen working area.
            if (moveToLocation.Y + tipSize.Height > screen.WorkingArea.Bottom)
            {
                moveToLocation.Y = screen.WorkingArea.Bottom - tipSize.Height;
            }

            SafeNativeMethods.SetWindowPos(new HandleRef(this, Handle),
            NativeMethods.HWND_TOPMOST,
            moveToLocation.X, moveToLocation.Y, tipSize.Width, tipSize.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOOWNERZORDER);
        }

        /// <summary>
        /// Handles the WM_MOVE message.
        /// </summary>
        private void WmMove()
        {
            var r = new NativeMethods.RECT();
            UnsafeNativeMethods.GetWindowRect(new HandleRef(this, Handle), ref r);
            var ti = new NativeMethods.TOOLINFO_TOOLTIP
            {
                cbSize = Marshal.SizeOf<NativeMethods.TOOLINFO_TOOLTIP>()
            };
            int ret = (int)UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_GETCURRENTTOOL, 0, ti);
            if (ret != 0)
            {
                IWin32Window win = (IWin32Window)_owners[ti.hwnd];
                if (win == null)
                {
                    win = (IWin32Window)Control.FromHandle(ti.hwnd);
                }

                if (win == null)
                {
                    return;
                }

                TipInfo tt = (TipInfo)_tools[win];
                if (win == null || tt == null)
                {
                    return;
                }

                // Treeview handles its own ToolTips.
                if (win is TreeView treeView)
                {
                    if (treeView.ShowNodeToolTips)
                    {
                        return;
                    }
                }

                // Reposition the tooltip when its about to be shown. since the tooltip can go out of screen workingarea bounds
                // Reposition would check the bounds for us.
                if (tt.Position != Point.Empty)
                {
                    Reposition(tt.Position, r.Size);
                }
            }
        }

        /// <summary>
        /// Handles the WM_MOUSEACTIVATE message.
        /// </summary>
        private void WmMouseActivate(ref Message msg)
        {
            var ti = new NativeMethods.TOOLINFO_TOOLTIP
            {
                cbSize = Marshal.SizeOf<NativeMethods.TOOLINFO_TOOLTIP>()
            };
            int ret = (int)UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_GETCURRENTTOOL, 0, ti);

            if (ret != 0)
            {

                IWin32Window win = (IWin32Window)_owners[ti.hwnd];
                if (win == null)
                {
                    win = (IWin32Window)Control.FromHandle(ti.hwnd);
                }

                if (win == null)
                {
                    return;
                }

                var r = new NativeMethods.RECT();
                UnsafeNativeMethods.GetWindowRect(new HandleRef(win, Control.GetSafeHandle(win)), ref r);
                Point cursorLocation = Cursor.Position;

                // Do not activate the mouse if its within the bounds of the
                // the associated tool
                if (cursorLocation.X >= r.left && cursorLocation.X <= r.right &&
                    cursorLocation.Y >= r.top && cursorLocation.Y <= r.bottom)
                {
                    msg.Result = (IntPtr)NativeMethods.MA_NOACTIVATE;
                }
            }
        }

        /// <summary>
        /// Handles the WM_WINDOWFROMPOINT message.
        /// </summary>
        private void WmWindowFromPoint(ref Message msg)
        {
            NativeMethods.POINT sc = (NativeMethods.POINT)msg.GetLParam(typeof(NativeMethods.POINT));
            Point screenCoords = new Point(sc.x, sc.y);
            bool result = false;
            msg.Result = GetWindowFromPoint(screenCoords, ref result);
        }

        /// <summary>
        /// Handles the TTN_SHOW message.
        /// </summary>
        private void WmShow()
        {
            // Get the bounds.
            var r = new NativeMethods.RECT();
            UnsafeNativeMethods.GetWindowRect(new HandleRef(this, Handle), ref r);

            var ti = new NativeMethods.TOOLINFO_TOOLTIP
            {
                cbSize = Marshal.SizeOf<NativeMethods.TOOLINFO_TOOLTIP>()
            };
            int ret = (int)UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_GETCURRENTTOOL, 0, ti);

            if (ret != 0)
            {
                IWin32Window win = (IWin32Window)_owners[ti.hwnd];
                if (win == null)
                {
                    win = (IWin32Window)Control.FromHandle(ti.hwnd);
                }

                if (win == null)
                {
                    return;
                }

                Control toolControl = win as Control;

                Size currentTooltipSize = r.Size;
                PopupEventArgs e = new PopupEventArgs(win, toolControl, IsBalloon, currentTooltipSize);
                OnPopup(e);

                if (toolControl is DataGridView dataGridView && dataGridView.CancelToolTipPopup(this))
                {
                    // The dataGridView cancelled the tooltip.
                    e.Cancel = true;
                }

                // We need to re-get the rectangle of the tooltip here because
                // any of the tooltip attributes/properties could have been updated
                // during the popup event; in which case the size of the tooltip is
                // affected. e.ToolTipSize is respected over r.Size
                UnsafeNativeMethods.GetWindowRect(new HandleRef(this, Handle), ref r);
                currentTooltipSize = (e.ToolTipSize == currentTooltipSize) ? r.Size : e.ToolTipSize;

                if (IsBalloon)
                {
                    // Get the text display rectangle
                    UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_ADJUSTRECT, 1, ref r);
                    if (r.Size.Height > currentTooltipSize.Height)
                    {
                        currentTooltipSize.Height = r.Size.Height;
                    }
                }

                // Set the max possible size of the tooltip to the size we received.
                // This prevents the operating system from drawing incorrect rectangles
                // when determing the correct display rectangle
                // Set the MaxWidth only if user has changed the width.
                if (currentTooltipSize != r.Size)
                {
                    Screen screen = Screen.FromPoint(Cursor.Position);
                    int maxwidth = (IsBalloon) ?
                    Math.Min(currentTooltipSize.Width - 2 * BalloonOffsetX, screen.WorkingArea.Width) :
                    Math.Min(currentTooltipSize.Width, screen.WorkingArea.Width);
                    UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETMAXTIPWIDTH, 0, maxwidth);
                }

                if (e.Cancel)
                {
                    _cancelled = true;
                    SafeNativeMethods.SetWindowPos(new HandleRef(this, Handle),
                    NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);

                }
                else
                {
                    _cancelled = false;
                    // Only width/height changes are respected, so set top,left to what we got earlier
                    SafeNativeMethods.SetWindowPos(new HandleRef(this, Handle),
                    NativeMethods.HWND_TOPMOST,
                    r.left, r.top, currentTooltipSize.Width, currentTooltipSize.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
                }
            }
        }

        /// <summary>
        /// Handles the WM_WINDOWPOSCHANGED message.
        /// We need to Hide the window since the native tooltip actually calls SetWindowPos in its TTN_SHOW even if we cancel showing the
        /// tooltip. Hence we need to listen to the WindowPosChanged message can hide the window ourselves.
        /// </summary>
        private bool WmWindowPosChanged()
        {
            if (_cancelled)
            {
                SafeNativeMethods.ShowWindow(new HandleRef(this, Handle), NativeMethods.SW_HIDE);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Handles the WM_WINDOWPOSCHANGING message.
        /// </summary>
        private unsafe void WmWindowPosChanging(ref Message m)
        {
            if (_cancelled || _isDisposing)
            {
                return;
            }

            NativeMethods.WINDOWPOS* wp = (NativeMethods.WINDOWPOS*)m.LParam;

            Cursor currentCursor = Cursor.CurrentInternal;
            Point cursorPos = Cursor.Position;

            var ti = new NativeMethods.TOOLINFO_TOOLTIP
            {
                cbSize = Marshal.SizeOf<NativeMethods.TOOLINFO_TOOLTIP>()
            };
            int ret = (int)UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_GETCURRENTTOOL, 0, ti);
            if (ret != 0)
            {
                IWin32Window win = (IWin32Window)_owners[ti.hwnd];
                if (win == null)
                {
                    win = (IWin32Window)Control.FromHandle(ti.hwnd);
                }

                if (win == null || !IsWindowActive(win))
                {
                    return;
                }

                TipInfo tt = null;
                if (win != null)
                {
                    tt = (TipInfo)_tools[win];
                    if (tt == null)
                    {
                        return;
                    }

                    // Treeview handles its own ToolTips.
                    if (win is TreeView treeView)
                    {
                        if (treeView.ShowNodeToolTips)
                        {
                            return;
                        }
                    }
                }

                if (IsBalloon)
                {
                    wp->cx += 2 * BalloonOffsetX;
                    return;
                }

                if ((tt.TipType & TipInfo.Type.Auto) != 0 && _window != null)
                {
                    _window.DefWndProc(ref m);
                    return;
                }

                if (((tt.TipType & TipInfo.Type.SemiAbsolute) != 0) && tt.Position == Point.Empty)
                {

                    Screen screen = Screen.FromPoint(cursorPos);
                    if (currentCursor != null)
                    {
                        wp->x = cursorPos.X;
                        wp->y = cursorPos.Y;
                        if (wp->y + wp->cy + currentCursor.Size.Height - currentCursor.HotSpot.Y > screen.WorkingArea.Bottom)
                        {
                            wp->y = cursorPos.Y - wp->cy;
                        }
                        else
                        {
                            wp->y = cursorPos.Y + currentCursor.Size.Height - currentCursor.HotSpot.Y;
                        }
                    }

                    if (wp->x + wp->cx > screen.WorkingArea.Right)
                    {
                        wp->x = screen.WorkingArea.Right - wp->cx;
                    }
                }
                else if ((tt.TipType & TipInfo.Type.SemiAbsolute) != 0 && tt.Position != Point.Empty)
                {

                    Screen screen = Screen.FromPoint(tt.Position);
                    wp->x = tt.Position.X;
                    if (wp->x + wp->cx > screen.WorkingArea.Right)
                    {
                        wp->x = screen.WorkingArea.Right - wp->cx;
                    }

                    wp->y = tt.Position.Y;
                    if (wp->y + wp->cy > screen.WorkingArea.Bottom)
                    {
                        wp->y = screen.WorkingArea.Bottom - wp->cy;
                    }
                }
            }

            m.Result = IntPtr.Zero;
        }

        /// <summary>
        /// Called just before the tooltip is hidden
        /// </summary>
        private void WmPop()
        {
            var ti = new NativeMethods.TOOLINFO_TOOLTIP
            {
                cbSize = Marshal.SizeOf<NativeMethods.TOOLINFO_TOOLTIP>()
            };
            int ret = (int)UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_GETCURRENTTOOL, 0, ti);
            if (ret != 0)
            {

                IWin32Window win = (IWin32Window)_owners[ti.hwnd];
                if (win == null)
                {
                    win = (IWin32Window)Control.FromHandle(ti.hwnd);
                }

                if (win == null)
                {
                    return;
                }

                Control control = win as Control;
                TipInfo tt = (TipInfo)_tools[win];
                if (tt == null)
                {
                    return;
                }

                // Must reset the maxwidth to the screen size.
                if ((tt.TipType & TipInfo.Type.Auto) != 0 || (tt.TipType & TipInfo.Type.SemiAbsolute) != 0)
                {
                    Screen screen = Screen.FromPoint(Cursor.Position);
                    UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_SETMAXTIPWIDTH, 0, screen.WorkingArea.Width);
                }

                // For non-auto tips (those showned through the show(.) methods, we need to
                // dissassociate them from the tip control.
                if ((tt.TipType & TipInfo.Type.Auto) == 0)
                {
                    _tools.Remove(control);
                    _owners.Remove(win.Handle);

                    control.HandleCreated -= new EventHandler(HandleCreated);
                    control.HandleDestroyed -= new EventHandler(HandleDestroyed);
                    _created.Remove(control);

                    if (_originalPopupDelay != 0)
                    {
                        AutoPopDelay = _originalPopupDelay;
                        _originalPopupDelay = 0;
                    }
                }
                else
                {
                    // Clear all other flags except for the Auto flag to ensure automatic tips can still show
                    tt.TipType = TipInfo.Type.Auto;
                    tt.Position = Point.Empty;
                    _tools[control] = tt;
                }
            }
        }

        /// <summary>
        /// WNDPROC
        /// </summary>
        private void WndProc(ref Message msg)
        {
            switch (msg.Msg)
            {
                case Interop.WindowMessages.WM_REFLECT + Interop.WindowMessages.WM_NOTIFY:
                    NativeMethods.NMHDR nmhdr = (NativeMethods.NMHDR)msg.GetLParam(typeof(NativeMethods.NMHDR));
                    if (nmhdr.code == NativeMethods.TTN_SHOW && !_trackPosition)
                    {
                        WmShow();
                    }
                    else if (nmhdr.code == NativeMethods.TTN_POP)
                    {
                        WmPop();
                        _window?.DefWndProc(ref msg);
                    }
                    break;

                case Interop.WindowMessages.WM_WINDOWPOSCHANGING:
                    WmWindowPosChanging(ref msg);
                    break;

                case Interop.WindowMessages.WM_WINDOWPOSCHANGED:
                    if (!WmWindowPosChanged() && _window != null)
                    {
                        _window.DefWndProc(ref msg);
                    }
                    break;

                case Interop.WindowMessages.WM_MOUSEACTIVATE:
                    WmMouseActivate(ref msg);
                    break;

                case Interop.WindowMessages.WM_MOVE:
                    WmMove();
                    break;

                case NativeMethods.TTM_WINDOWFROMPOINT:
                    WmWindowFromPoint(ref msg);
                    break;

                case Interop.WindowMessages.WM_PRINTCLIENT:
                    goto case Interop.WindowMessages.WM_PAINT;

                case Interop.WindowMessages.WM_PAINT:
                    if (OwnerDraw && !_isBalloon && !_trackPosition)
                    {
                        NativeMethods.PAINTSTRUCT ps = new NativeMethods.PAINTSTRUCT();
                        IntPtr dc = UnsafeNativeMethods.BeginPaint(new HandleRef(this, Handle), ref ps);
                        Graphics g = Graphics.FromHdcInternal(dc);
                        try
                        {
                            Rectangle bounds = new Rectangle(ps.rcPaint_left, ps.rcPaint_top,
                            ps.rcPaint_right - ps.rcPaint_left,
                            ps.rcPaint_bottom - ps.rcPaint_top);
                            if (bounds == Rectangle.Empty)
                            {
                                return;
                            }
                            var ti = new NativeMethods.TOOLINFO_TOOLTIP
                            {
                                cbSize = Marshal.SizeOf<NativeMethods.TOOLINFO_TOOLTIP>()
                            };
                            int ret = unchecked((int)(long)UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), NativeMethods.TTM_GETCURRENTTOOL, 0, ti));
                            if (ret != 0)
                            {
                                IWin32Window win = (IWin32Window)_owners[ti.hwnd];
                                Control ac = Control.FromHandle(ti.hwnd);
                                if (win == null)
                                {
                                    win = (IWin32Window)ac;
                                }
                                Font font;
                                try
                                {
                                    font = Font.FromHfont(UnsafeNativeMethods.SendMessage(new HandleRef(this, Handle), Interop.WindowMessages.WM_GETFONT, 0, 0));
                                }
                                catch (ArgumentException)
                                {
                                    // If the current default tooltip font is a non-TrueType font, then
                                    // Font.FromHfont throws this exception, so fall back to the default control font.
                                    font = Control.DefaultFont;
                                }

                                OnDraw(new DrawToolTipEventArgs(g, win, ac, bounds, GetToolTip(ac),
                                                                BackColor, ForeColor, font));

                                break;
                            }
                        }
                        finally
                        {
                            g.Dispose();
                            UnsafeNativeMethods.EndPaint(new HandleRef(this, Handle), ref ps);
                        }
                    }

                    // If not OwnerDraw, fall through
                    goto default;
                default:
                    _window?.DefWndProc(ref msg);
                    break;
            }
        }

        private class ToolTipNativeWindow : NativeWindow
        {
            private readonly ToolTip _control;

            internal ToolTipNativeWindow(ToolTip control)
            {
                _control = control;
            }

            protected override void WndProc(ref Message m) => _control?.WndProc(ref m);
        }

        private class ToolTipTimer : Timer
        {
            public ToolTipTimer(IWin32Window owner) : base()
            {
                Host = owner;
            }

            public IWin32Window Host { get; }
        }

        private class TipInfo
        {
            [Flags]
            public enum Type
            {
                None = 0x0000,
                Auto = 0x0001,
                Absolute = 0x0002,
                SemiAbsolute = 0x0004
            }

            public Type TipType { get; set; } = Type.Auto;
            private string _caption;
            private readonly string _designerText;
            public Point Position { get; set; }

            public TipInfo(string caption, Type type)
            {
                _caption = caption;
                TipType = type;
                if (type == Type.Auto)
                {
                    _designerText = caption;
                }
            }

            public string Caption
            {
                get => ((TipType & (Type.Absolute | Type.SemiAbsolute)) != 0) ? _caption : _designerText;
                set => _caption = value;
            }
        }
    }
}
