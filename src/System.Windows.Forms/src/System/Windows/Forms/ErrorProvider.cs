﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms
{
    /// <summary>
    ///  ErrorProvider presents a simple user interface for indicating to the
    ///  user that a control on a form has an error associated with it.  If a
    ///  error description string is specified for the control, then an icon
    ///  will appear next to the control, and when the mouse hovers over the
    ///  icon, a tooltip will appear showing the error description string.
    /// </summary>
    [
        ProvideProperty("IconPadding", typeof(Control)),
        ProvideProperty("IconAlignment", typeof(Control)),
        ProvideProperty("Error", typeof(Control)),
        ToolboxItemFilter("System.Windows.Forms"),
        ComplexBindingProperties(nameof(DataSource), nameof(DataMember)),
        SRDescription(nameof(SR.DescriptionErrorProvider))
    ]
    public class ErrorProvider : Component, IExtenderProvider, ISupportInitialize
    {
        //
        // FIELDS
        //

        readonly Hashtable items = new Hashtable();
        readonly Hashtable windows = new Hashtable();
        Icon icon = DefaultIcon;
        IconRegion region;
        int itemIdCounter;
        int blinkRate;
        ErrorBlinkStyle blinkStyle;
        bool showIcon = true;                       // used for blinking
        private bool inSetErrorManager = false;
        private bool setErrorManagerOnEndInit = false;
        private bool initializing = false;
        [ThreadStatic]
        static Icon defaultIcon = null;
        const int defaultBlinkRate = 250;
        const ErrorBlinkStyle defaultBlinkStyle = ErrorBlinkStyle.BlinkIfDifferentError;
        const ErrorIconAlignment defaultIconAlignment = ErrorIconAlignment.MiddleRight;

        // data binding
        private ContainerControl parentControl;
        private object dataSource = null;
        private string dataMember = null;
        private BindingManagerBase errorManager;
        private readonly EventHandler currentChanged;

        // listen to the OnPropertyChanged event in the ContainerControl
        private readonly EventHandler propChangedEvent;

        private EventHandler onRightToLeftChanged;
        private bool rightToLeft = false;

        private object userData;

        //
        // CONSTRUCTOR
        //

        /// <summary>
        ///  Default constructor.
        /// </summary>
        public ErrorProvider()
        {
            icon = DefaultIcon;
            blinkRate = defaultBlinkRate;
            blinkStyle = defaultBlinkStyle;
            currentChanged = new EventHandler(ErrorManager_CurrentChanged);
        }

        public ErrorProvider(ContainerControl parentControl) : this()
        {
            this.parentControl = parentControl;
            propChangedEvent = new EventHandler(ParentControl_BindingContextChanged);
            parentControl.BindingContextChanged += propChangedEvent;
        }

        public ErrorProvider(IContainer container) : this()
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            container.Add(this);
        }

        //
        // PROPERTIES
        //

        public override ISite Site
        {
            set
            {
                base.Site = value;
                if (value == null)
                {
                    return;
                }

                if (value.GetService(typeof(IDesignerHost)) is IDesignerHost host)
                {
                    IComponent baseComp = host.RootComponent;

                    if (baseComp is ContainerControl)
                    {
                        ContainerControl = (ContainerControl)baseComp;
                    }
                }
            }
        }

        /// <summary>
        ///  Returns or sets when the error icon flashes.
        /// </summary>
        [
        SRCategory(nameof(SR.CatBehavior)),
        DefaultValue(defaultBlinkStyle),
        SRDescription(nameof(SR.ErrorProviderBlinkStyleDescr))
        ]
        public ErrorBlinkStyle BlinkStyle
        {
            get
            {
                if (blinkRate == 0)
                {
                    return ErrorBlinkStyle.NeverBlink;
                }
                return blinkStyle;
            }
            set
            {
                //valid values are 0x0 to 0x2
                if (!ClientUtils.IsEnumValid(value, (int)value, (int)ErrorBlinkStyle.BlinkIfDifferentError, (int)ErrorBlinkStyle.NeverBlink))
                {
                    throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ErrorBlinkStyle));
                }

                // If the blinkRate == 0, then set blinkStyle = neverBlink
                //
                if (blinkRate == 0)
                {
                    value = ErrorBlinkStyle.NeverBlink;
                }

                if (blinkStyle == value)
                {
                    return;
                }

                if (value == ErrorBlinkStyle.AlwaysBlink)
                {
                    // we need to startBlinking on all the controlItems
                    // in our items hashTable.
                    showIcon = true;
                    blinkStyle = ErrorBlinkStyle.AlwaysBlink;
                    foreach (ErrorWindow w in windows.Values)
                    {
                        w.StartBlinking();
                    }
                }
                else if (blinkStyle == ErrorBlinkStyle.AlwaysBlink)
                {
                    // we need to stop blinking...
                    blinkStyle = value;
                    foreach (ErrorWindow w in windows.Values)
                    {
                        w.StopBlinking();
                    }
                }
                else
                {
                    blinkStyle = value;
                }
            }
        }

        /// <summary>
        ///  Indicates what container control (usually the form) should be inspected for bindings.
        ///  A binding will reveal what control to place errors on for a
        ///  error in the current row in the DataSource/DataMember pair.
        /// </summary>
        [
        DefaultValue(null),
        SRCategory(nameof(SR.CatData)),
        SRDescription(nameof(SR.ErrorProviderContainerControlDescr))
        ]
        public ContainerControl ContainerControl
        {
            get
            {
                return parentControl;
            }
            set
            {
                if (parentControl != value)
                {
                    if (parentControl != null)
                    {
                        parentControl.BindingContextChanged -= propChangedEvent;
                    }

                    parentControl = value;

                    if (parentControl != null)
                    {
                        parentControl.BindingContextChanged += propChangedEvent;
                    }

                    Set_ErrorManager(DataSource, DataMember, true);
                }
            }
        }

        /// <summary>
        ///  This is used for international applications where the language
        ///  is written from RightToLeft. When this property is true,
        //      text will be from right to left.
        /// </summary>
        [
        SRCategory(nameof(SR.CatAppearance)),
        Localizable(true),
        DefaultValue(false),
        SRDescription(nameof(SR.ControlRightToLeftDescr))
        ]
        public virtual bool RightToLeft
        {
            get
            {

                return rightToLeft;
            }

            set
            {
                if (value != rightToLeft)
                {
                    rightToLeft = value;
                    OnRightToLeftChanged(EventArgs.Empty);
                }
            }
        }

        [SRCategory(nameof(SR.CatPropertyChanged)), SRDescription(nameof(SR.ControlOnRightToLeftChangedDescr))]
        public event EventHandler RightToLeftChanged
        {
            add => onRightToLeftChanged += value;
            remove => onRightToLeftChanged -= value;
        }

        /// <summary>
        ///  User defined data associated with the control.
        /// </summary>
        [
        SRCategory(nameof(SR.CatData)),
        Localizable(false),
        Bindable(true),
        SRDescription(nameof(SR.ControlTagDescr)),
        DefaultValue(null),
        TypeConverter(typeof(StringConverter)),
        ]
        public object Tag
        {
            get
            {
                return userData;
            }
            set
            {
                userData = value;
            }
        }

        private void Set_ErrorManager(object newDataSource, string newDataMember, bool force)
        {
            if (inSetErrorManager)
            {
                return;
            }

            inSetErrorManager = true;
            try
            {
                bool dataSourceChanged = DataSource != newDataSource;
                bool dataMemberChanged = DataMember != newDataMember;

                //if nothing changed, then do not do any work
                //
                if (!dataSourceChanged && !dataMemberChanged && !force)
                {
                    return;
                }

                // set the dataSource and the dataMember
                //
                dataSource = newDataSource;
                dataMember = newDataMember;

                if (initializing)
                {
                    setErrorManagerOnEndInit = true;
                }
                else
                {
                    // unwire the errorManager:
                    //
                    UnwireEvents(errorManager);

                    // get the new errorManager
                    //
                    if (parentControl != null && dataSource != null && parentControl.BindingContext != null)
                    {
                        errorManager = parentControl.BindingContext[dataSource, dataMember];
                    }
                    else
                    {
                        errorManager = null;
                    }

                    // wire the events
                    //
                    WireEvents(errorManager);

                    // see if there are errors at the current
                    // item in the list, w/o waiting for the position to change
                    if (errorManager != null)
                    {
                        UpdateBinding();
                    }
                }
            }
            finally
            {
                inSetErrorManager = false;
            }
        }

        /// <summary>
        ///  Indicates the source of data to bind errors against.
        /// </summary>
        [
        DefaultValue(null),
        SRCategory(nameof(SR.CatData)),
        AttributeProvider(typeof(IListSource)),
        SRDescription(nameof(SR.ErrorProviderDataSourceDescr))
        ]
        public object DataSource
        {
            get
            {
                return dataSource;
            }
            set
            {
                if (parentControl != null && value != null && !string.IsNullOrEmpty(dataMember))
                {
                    // Let's check if the datamember exists in the new data source
                    try
                    {
                        errorManager = parentControl.BindingContext[value, dataMember];
                    }
                    catch (ArgumentException)
                    {
                        // The data member doesn't exist in the data source, so set it to null
                        dataMember = string.Empty;
                    }
                }

                Set_ErrorManager(value, DataMember, false);
            }
        }

        private bool ShouldSerializeDataSource()
        {
            return (dataSource != null);
        }

        /// <summary>
        ///  Indicates the sub-list of data from the DataSource to bind errors against.
        /// </summary>
        [
        DefaultValue(null),
        SRCategory(nameof(SR.CatData)),
        Editor("System.Windows.Forms.Design.DataMemberListEditor, " + AssemblyRef.SystemDesign, typeof(Drawing.Design.UITypeEditor)),
        SRDescription(nameof(SR.ErrorProviderDataMemberDescr))
        ]
        public string DataMember
        {
            get
            {
                return dataMember;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }

                Set_ErrorManager(DataSource, value, false);
            }
        }

        private bool ShouldSerializeDataMember()
        {
            return (dataMember != null && dataMember.Length != 0);
        }

        public void BindToDataAndErrors(object newDataSource, string newDataMember)
        {
            Set_ErrorManager(newDataSource, newDataMember, false);
        }

        private void WireEvents(BindingManagerBase listManager)
        {
            if (listManager != null)
            {
                listManager.CurrentChanged += currentChanged;
                listManager.BindingComplete += new BindingCompleteEventHandler(ErrorManager_BindingComplete);

                if (listManager is CurrencyManager currManager)
                {
                    currManager.ItemChanged += new ItemChangedEventHandler(ErrorManager_ItemChanged);
                    currManager.Bindings.CollectionChanged += new CollectionChangeEventHandler(ErrorManager_BindingsChanged);
                }
            }
        }

        private void UnwireEvents(BindingManagerBase listManager)
        {
            if (listManager != null)
            {
                listManager.CurrentChanged -= currentChanged;
                listManager.BindingComplete -= new BindingCompleteEventHandler(ErrorManager_BindingComplete);

                if (listManager is CurrencyManager currManager)
                {
                    currManager.ItemChanged -= new ItemChangedEventHandler(ErrorManager_ItemChanged);
                    currManager.Bindings.CollectionChanged -= new CollectionChangeEventHandler(ErrorManager_BindingsChanged);
                }
            }
        }

        private void ErrorManager_BindingComplete(object sender, BindingCompleteEventArgs e)
        {
            Binding binding = e.Binding;

            if (binding != null && binding.Control != null)
            {
                SetError(binding.Control, (e.ErrorText ?? string.Empty));
            }
        }

        private void ErrorManager_BindingsChanged(object sender, CollectionChangeEventArgs e)
        {
            ErrorManager_CurrentChanged(errorManager, e);
        }

        private void ParentControl_BindingContextChanged(object sender, EventArgs e)
        {
            Set_ErrorManager(DataSource, DataMember, true);
        }

        // Work around... we should figure out if errors changed automatically.
        public void UpdateBinding()
        {
            ErrorManager_CurrentChanged(errorManager, EventArgs.Empty);
        }

        private void ErrorManager_ItemChanged(object sender, ItemChangedEventArgs e)
        {
            BindingsCollection errBindings = errorManager.Bindings;
            int bindingsCount = errBindings.Count;

            // If the list became empty then reset the errors
            if (e.Index == -1 && errorManager.Count == 0)
            {
                for (int j = 0; j < bindingsCount; j++)
                {
                    if (errBindings[j].Control != null)
                    {
                        // ...ignore everything but bindings to Controls
                        SetError(errBindings[j].Control, "");
                    }
                }
            }
            else
            {
                ErrorManager_CurrentChanged(sender, e);
            }
        }

        private void ErrorManager_CurrentChanged(object sender, EventArgs e)
        {
            Debug.Assert(sender == errorManager, "who else can send us messages?");

            // flush the old list
            //
            // items.Clear();

            if (errorManager.Count == 0)
            {
                return;
            }

            object value = errorManager.Current;
            if (!(value is IDataErrorInfo))
            {
                return;
            }

            BindingsCollection errBindings = errorManager.Bindings;
            int bindingsCount = errBindings.Count;

            // we need to delete the blinkPhases from each controlItem (suppose
            // that the error that we get is the same error. then we want to
            // show the error and not blink )
            //
            foreach (ControlItem ctl in items.Values)
            {
                ctl.BlinkPhase = 0;
            }

            // We can only show one error per control, so we will build up a string...
            //
            Hashtable controlError = new Hashtable(bindingsCount);

            for (int j = 0; j < bindingsCount; j++)
            {

                // Ignore everything but bindings to Controls
                if (errBindings[j].Control == null)
                {
                    continue;
                }

                Binding dataBinding = errBindings[j];
                string error = ((IDataErrorInfo)value)[dataBinding.BindingMemberInfo.BindingField];

                if (error == null)
                {
                    error = string.Empty;
                }

                string outputError = string.Empty;

                if (controlError.Contains(dataBinding.Control))
                {
                    outputError = (string)controlError[dataBinding.Control];
                }

                // Utilize the error string without including the field name.
                if (string.IsNullOrEmpty(outputError))
                {
                    outputError = error;
                }
                else
                {
                    outputError = string.Concat(outputError, "\r\n", error);
                }

                controlError[dataBinding.Control] = outputError;
            }

            IEnumerator enumerator = controlError.GetEnumerator();
            while (enumerator.MoveNext())
            {
                DictionaryEntry entry = (DictionaryEntry)enumerator.Current;
                SetError((Control)entry.Key, (string)entry.Value);
            }
        }

        /// <summary>
        ///  Returns or set the rate in milliseconds at which the error icon flashes.
        /// </summary>
        [
        SRCategory(nameof(SR.CatBehavior)),
        DefaultValue(defaultBlinkRate),
        SRDescription(nameof(SR.ErrorProviderBlinkRateDescr)),
        RefreshProperties(RefreshProperties.Repaint)
        ]
        public int BlinkRate
        {
            get
            {
                return blinkRate;
            }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(BlinkRate), value, SR.BlinkRateMustBeZeroOrMore);
                }
                blinkRate = value;
                // If we set the blinkRate = 0 then set BlinkStyle = NeverBlink
                if (blinkRate == 0)
                {
                    BlinkStyle = ErrorBlinkStyle.NeverBlink;
                }
            }
        }

        /// <summary>
        ///  Demand load and cache the default icon.
        /// </summary>
        static Icon DefaultIcon
        {
            get
            {
                if (defaultIcon == null)
                {
                    lock (typeof(ErrorProvider))
                    {
                        if (defaultIcon == null)
                        {
                            defaultIcon = new Icon(typeof(ErrorProvider), "Error");
                        }
                    }
                }
                return defaultIcon;
            }
        }

        /// <summary>
        ///  Returns or sets the Icon that displayed next to a control when an error
        ///  description string has been set for the control.  For best results, an
        ///  icon containing a 16 by 16 icon should be used.
        /// </summary>
        [
        Localizable(true),
        SRCategory(nameof(SR.CatAppearance)),
        SRDescription(nameof(SR.ErrorProviderIconDescr))
        ]
        public Icon Icon
        {
            get
            {
                return icon;
            }
            set
            {
                icon = value ?? throw new ArgumentNullException(nameof(value));
                DisposeRegion();
                ErrorWindow[] array = new ErrorWindow[windows.Values.Count];
                windows.Values.CopyTo(array, 0);
                for (int i = 0; i < array.Length; i++)
                {
                    array[i].Update(false /*timerCaused*/);
                }
            }
        }

        /// <summary>
        ///  Create the icon region on demand.
        /// </summary>
        internal IconRegion Region
        {
            get
            {
                if (region == null)
                {
                    region = new IconRegion(Icon);
                }

                return region;
            }
        }

        //
        // METHODS
        //

        // Begin bulk member initialization - deferring binding to data source until EndInit is reached
        void ISupportInitialize.BeginInit()
        {
            initializing = true;
        }

        // End bulk member initialization by binding to data source
        private void EndInitCore()
        {
            initializing = false;

            if (setErrorManagerOnEndInit)
            {
                setErrorManagerOnEndInit = false;
                Set_ErrorManager(DataSource, DataMember, true);
            }
        }

        // Check to see if DataSource has completed its initialization, before ending our initialization.
        // If DataSource is still initializing, hook its Initialized event and wait for it to signal completion.
        // If DataSource is already initialized, just go ahead and complete our initialization now.
        //
        void ISupportInitialize.EndInit()
        {
            ISupportInitializeNotification dsInit = (DataSource as ISupportInitializeNotification);

            if (dsInit != null && !dsInit.IsInitialized)
            {
                dsInit.Initialized += new EventHandler(DataSource_Initialized);
            }
            else
            {
                EndInitCore();
            }
        }

        // Respond to late completion of the DataSource's initialization, by completing our own initialization.
        // This situation can arise if the call to the DataSource's EndInit() method comes after the call to the
        // BindingSource's EndInit() method (since code-generated ordering of these calls is non-deterministic).
        //
        private void DataSource_Initialized(object sender, EventArgs e)
        {
            ISupportInitializeNotification dsInit = (DataSource as ISupportInitializeNotification);

            Debug.Assert(dsInit != null, "ErrorProvider: ISupportInitializeNotification.Initialized event received, but current DataSource does not support ISupportInitializeNotification!");
            Debug.Assert(dsInit.IsInitialized, "ErrorProvider: DataSource sent ISupportInitializeNotification.Initialized event but before it had finished initializing.");

            if (dsInit != null)
            {
                dsInit.Initialized -= new EventHandler(DataSource_Initialized);
            }

            EndInitCore();
        }

        /// <summary>
        ///  Clears all errors being tracked by this error provider, ie. undoes all previous calls to SetError.
        /// </summary>
        public void Clear()
        {
            ErrorWindow[] w = new ErrorWindow[windows.Values.Count];
            windows.Values.CopyTo(w, 0);
            for (int i = 0; i < w.Length; i++)
            {
                w[i].Dispose();
            }
            windows.Clear();
            foreach (ControlItem item in items.Values)
            {
                if (item != null)
                {
                    item.Dispose();
                }
            }
            items.Clear();
        }

        /// <summary>
        ///  Returns whether a control can be extended.
        /// </summary>
        public bool CanExtend(object extendee)
        {
            return extendee is Control && !(extendee is Form) && !(extendee is ToolBar);
        }

        /// <summary>
        ///  Release any resources that this component is using.  After calling Dispose,
        ///  the component should no longer be used.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Clear();
                DisposeRegion();
                UnwireEvents(errorManager);
            }
            base.Dispose(disposing);
        }

        /// <summary>
        ///  Helper to dispose the cached icon region.
        /// </summary>
        void DisposeRegion()
        {
            if (region != null)
            {
                region.Dispose();
                region = null;
            }
        }

        /// <summary>
        ///  Helper to make sure we have allocated a control item for this control.
        /// </summary>
        private ControlItem EnsureControlItem(Control control)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            ControlItem item = (ControlItem)items[control];
            if (item == null)
            {
                item = new ControlItem(this, control, (IntPtr)(++itemIdCounter));
                items[control] = item;
            }
            return item;
        }

        /// <summary>
        ///  Helper to make sure we have allocated an error window for this control.
        /// </summary>
        internal ErrorWindow EnsureErrorWindow(Control parent)
        {
            ErrorWindow window = (ErrorWindow)windows[parent];
            if (window == null)
            {
                window = new ErrorWindow(this, parent);
                windows[parent] = window;
            }
            return window;
        }

        /// <summary>
        ///  Returns the current error description string for the specified control.
        /// </summary>
        [
        DefaultValue(""),
        Localizable(true),
        SRCategory(nameof(SR.CatAppearance)),
        SRDescription(nameof(SR.ErrorProviderErrorDescr))
        ]
        public string GetError(Control control)
        {
            return EnsureControlItem(control).Error;
        }

        /// <summary>
        ///  Returns where the error icon should be placed relative to the control.
        /// </summary>
        [
        DefaultValue(defaultIconAlignment),
        Localizable(true),
        SRCategory(nameof(SR.CatAppearance)),
        SRDescription(nameof(SR.ErrorProviderIconAlignmentDescr))
        ]
        public ErrorIconAlignment GetIconAlignment(Control control)
        {
            return EnsureControlItem(control).IconAlignment;
        }

        /// <summary>
        ///  Returns the amount of extra space to leave next to the error icon.
        /// </summary>
        [
        DefaultValue(0),
        Localizable(true),
        SRCategory(nameof(SR.CatAppearance)),
        SRDescription(nameof(SR.ErrorProviderIconPaddingDescr))
        ]
        public int GetIconPadding(Control control)
        {
            return EnsureControlItem(control).IconPadding;
        }

        private void ResetIcon()
        {
            Icon = DefaultIcon;
        }

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        protected virtual void OnRightToLeftChanged(EventArgs e)
        {
            foreach (ErrorWindow w in windows.Values)
            {
                w.Update(false);
            }

            onRightToLeftChanged?.Invoke(this, e);
        }

        /// <summary>
        ///  Sets the error description string for the specified control.
        /// </summary>
        public void SetError(Control control, string value)
        {
            EnsureControlItem(control).Error = value;
        }

        /// <summary>
        ///  Sets where the error icon should be placed relative to the control.
        /// </summary>
        public void SetIconAlignment(Control control, ErrorIconAlignment value)
        {
            EnsureControlItem(control).IconAlignment = value;
        }

        /// <summary>
        ///  Sets the amount of extra space to leave next to the error icon.
        /// </summary>
        public void SetIconPadding(Control control, int padding)
        {
            EnsureControlItem(control).IconPadding = padding;
        }

        private bool ShouldSerializeIcon()
        {
            return Icon != DefaultIcon;
        }

        /// <summary>
        ///  There is one ErrorWindow for each control parent.  It is parented to the
        ///  control parent.  The window's region is made up of the regions from icons
        ///  of all child icons.  The window's size is the enclosing rectangle for all
        ///  the regions.  A tooltip window is created as a child of this window.  The
        ///  rectangle associated with each error icon being displayed is added as a
        ///  tool to the tooltip window.
        /// </summary>
        internal class ErrorWindow : NativeWindow
        {
            //
            // FIELDS
            //

            readonly ArrayList items = new ArrayList();
            readonly Control parent;
            readonly ErrorProvider provider;
            Rectangle windowBounds = Rectangle.Empty;
            Timer timer;
            NativeWindow tipWindow;

            DeviceContext mirrordc = null;
            Size mirrordcExtent = Size.Empty;
            Point mirrordcOrigin = Point.Empty;
            DeviceContextMapMode mirrordcMode = DeviceContextMapMode.Text;

            //
            // CONSTRUCTORS
            //

            /// <summary>
            ///  Construct an error window for this provider and control parent.
            /// </summary>
            public ErrorWindow(ErrorProvider provider, Control parent)
            {
                this.provider = provider;
                this.parent = parent;
            }

            //
            // METHODS
            //

            /// <summary>
            ///  This is called when a control would like to show an error icon.
            /// </summary>
            public void Add(ControlItem item)
            {
                items.Add(item);
                if (!EnsureCreated())
                {
                    return;
                }

                NativeMethods.TOOLINFO_T toolInfo = new NativeMethods.TOOLINFO_T();
                toolInfo.cbSize = Marshal.SizeOf(toolInfo);
                toolInfo.hwnd = Handle;
                toolInfo.uId = item.Id;
                toolInfo.lpszText = item.Error;
                toolInfo.uFlags = NativeMethods.TTF_SUBCLASS;
                UnsafeNativeMethods.SendMessage(new HandleRef(tipWindow, tipWindow.Handle), NativeMethods.TTM_ADDTOOL, 0, toolInfo);

                Update(false /*timerCaused*/);
            }

            /// <summary>
            ///  Called to get rid of any resources the Object may have.
            /// </summary>
            public void Dispose()
            {
                EnsureDestroyed();
            }

            /// <summary>
            ///  Make sure the error window is created, and the tooltip window is created.
            /// </summary>
            bool EnsureCreated()
            {
                if (Handle == IntPtr.Zero)
                {
                    if (!parent.IsHandleCreated)
                    {
                        return false;
                    }
                    CreateParams cparams = new CreateParams
                    {
                        Caption = string.Empty,
                        Style = NativeMethods.WS_VISIBLE | NativeMethods.WS_CHILD,
                        ClassStyle = (int)NativeMethods.ClassStyle.CS_DBLCLKS,
                        X = 0,
                        Y = 0,
                        Width = 0,
                        Height = 0,
                        Parent = parent.Handle
                    };

                    CreateHandle(cparams);

                    NativeMethods.INITCOMMONCONTROLSEX icc = new NativeMethods.INITCOMMONCONTROLSEX
                    {
                        dwICC = NativeMethods.ICC_TAB_CLASSES
                    };
                    icc.dwSize = Marshal.SizeOf(icc);
                    SafeNativeMethods.InitCommonControlsEx(icc);
                    cparams = new CreateParams
                    {
                        Parent = Handle,
                        ClassName = NativeMethods.TOOLTIPS_CLASS,
                        Style = NativeMethods.TTS_ALWAYSTIP
                    };
                    tipWindow = new NativeWindow();
                    tipWindow.CreateHandle(cparams);

                    UnsafeNativeMethods.SendMessage(new HandleRef(tipWindow, tipWindow.Handle), NativeMethods.TTM_SETMAXTIPWIDTH, 0, SystemInformation.MaxWindowTrackSize.Width);
                    SafeNativeMethods.SetWindowPos(new HandleRef(tipWindow, tipWindow.Handle), NativeMethods.HWND_TOP, 0, 0, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
                    UnsafeNativeMethods.SendMessage(new HandleRef(tipWindow, tipWindow.Handle), NativeMethods.TTM_SETDELAYTIME, NativeMethods.TTDT_INITIAL, 0);
                }
                return true;
            }

            /// <summary>
            ///  Destroy the timer, toolwindow, and the error window itself.
            /// </summary>
            void EnsureDestroyed()
            {
                if (timer != null)
                {
                    timer.Dispose();
                    timer = null;
                }
                if (tipWindow != null)
                {
                    tipWindow.DestroyHandle();
                    tipWindow = null;
                }

                // Hide the window and invalidate the parent to ensure
                // that we leave no visual artifacts... given that we
                // have a bizare region window, this is needed.
                //
                SafeNativeMethods.SetWindowPos(new HandleRef(this, Handle),
                                               NativeMethods.HWND_TOP,
                                               windowBounds.X,
                                               windowBounds.Y,
                                               windowBounds.Width,
                                               windowBounds.Height,
                                               NativeMethods.SWP_HIDEWINDOW
                                               | NativeMethods.SWP_NOSIZE
                                               | NativeMethods.SWP_NOMOVE);
                if (parent != null)
                {
                    parent.Invalidate(true);
                }
                DestroyHandle();

                Debug.Assert(mirrordc == null, "Why is mirrordc non-null?");
                if (mirrordc != null)
                {
                    mirrordc.Dispose();
                }
            }

            /// <summary>
            ///
            /// Since we added mirroring to certain controls, we need to make sure the
            /// error icons show up in the correct place. We cannot mirror the errorwindow
            /// in EnsureCreated (although that would have been really easy), since we use
            /// GDI+ for some of this code, and as we all know, GDI+ does not handle mirroring
            /// at all.
            ///
            /// To work around that we create our own mirrored dc when we need to.
            ///
            /// </summary>
            void CreateMirrorDC(IntPtr hdc, int originOffset)
            {

                Debug.Assert(mirrordc == null, "Why is mirrordc non-null? Did you not call RestoreMirrorDC?");

                mirrordc = DeviceContext.FromHdc(hdc);
                if (parent.IsMirrored && mirrordc != null)
                {
                    mirrordc.SaveHdc();
                    mirrordcExtent = mirrordc.ViewportExtent;
                    mirrordcOrigin = mirrordc.ViewportOrigin;

                    mirrordcMode = mirrordc.SetMapMode(DeviceContextMapMode.Anisotropic);
                    mirrordc.ViewportExtent = new Size(-(mirrordcExtent.Width), mirrordcExtent.Height);
                    mirrordc.ViewportOrigin = new Point(mirrordcOrigin.X + originOffset, mirrordcOrigin.Y);
                }
            }

            void RestoreMirrorDC()
            {

                if (parent.IsMirrored && mirrordc != null)
                {
                    mirrordc.ViewportExtent = mirrordcExtent;
                    mirrordc.ViewportOrigin = mirrordcOrigin;
                    mirrordc.SetMapMode(mirrordcMode);
                    mirrordc.RestoreHdc();
                    mirrordc.Dispose();
                }

                mirrordc = null;
                mirrordcExtent = Size.Empty;
                mirrordcOrigin = Point.Empty;
                mirrordcMode = DeviceContextMapMode.Text;
            }

            /// <summary>
            ///  This is called when the error window needs to paint.  We paint each icon at its
            ///  correct location.
            /// </summary>
            void OnPaint(ref Message m)
            {
                NativeMethods.PAINTSTRUCT ps = new NativeMethods.PAINTSTRUCT();
                IntPtr hdc = UnsafeNativeMethods.BeginPaint(new HandleRef(this, Handle), ref ps);
                try
                {
                    CreateMirrorDC(hdc, windowBounds.Width - 1);

                    try
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            ControlItem item = (ControlItem)items[i];
                            Rectangle bounds = item.GetIconBounds(provider.Region.Size);
                            SafeNativeMethods.DrawIconEx(new HandleRef(this, mirrordc.Hdc), bounds.X - windowBounds.X, bounds.Y - windowBounds.Y, new HandleRef(provider.Region, provider.Region.IconHandle), bounds.Width, bounds.Height, 0, NativeMethods.NullHandleRef, NativeMethods.DI_NORMAL);
                        }
                    }
                    finally
                    {
                        RestoreMirrorDC();
                    }
                }
                finally
                {
                    UnsafeNativeMethods.EndPaint(new HandleRef(this, Handle), ref ps);
                }
            }

            protected override void OnThreadException(Exception e)
            {
                Application.OnThreadException(e);
            }

            /// <summary>
            ///  This is called when an error icon is flashing, and the view needs to be updatd.
            /// </summary>
            void OnTimer(object sender, EventArgs e)
            {
                int blinkPhase = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    blinkPhase += ((ControlItem)items[i]).BlinkPhase;
                }
                if (blinkPhase == 0 && provider.BlinkStyle != ErrorBlinkStyle.AlwaysBlink)
                {
                    Debug.Assert(timer != null);
                    timer.Stop();
                }
                Update(true /*timerCaused*/);
            }

            private void OnToolTipVisibilityChanging(IntPtr id, bool toolTipShown)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (((ControlItem)items[i]).Id == id)
                    {
                        ((ControlItem)items[i]).ToolTipShown = toolTipShown;
                    }
                }
#if DEBUG
                int shownTooltips = 0;
                for (int j = 0; j < items.Count; j++)
                {
                    if (((ControlItem)items[j]).ToolTipShown)
                    {
                        shownTooltips++;
                    }
                }
                Debug.Assert(shownTooltips <= 1);
#endif
            }

            /// <summary>
            ///  This is called when a control no longer needs to display an error icon.
            /// </summary>
            public void Remove(ControlItem item)
            {
                items.Remove(item);

                if (tipWindow != null)
                {
                    NativeMethods.TOOLINFO_T toolInfo = new NativeMethods.TOOLINFO_T();
                    toolInfo.cbSize = Marshal.SizeOf(toolInfo);
                    toolInfo.hwnd = Handle;
                    toolInfo.uId = item.Id;
                    UnsafeNativeMethods.SendMessage(new HandleRef(tipWindow, tipWindow.Handle), NativeMethods.TTM_DELTOOL, 0, toolInfo);
                }

                if (items.Count == 0)
                {
                    EnsureDestroyed();
                }
                else
                {
                    Update(false /*timerCaused*/);
                }
            }

            /// <summary>
            ///  Start the blinking process.  The timer will fire until there are no more
            ///  icons that need to blink.
            /// </summary>
            internal void StartBlinking()
            {
                if (timer == null)
                {
                    timer = new Timer();
                    timer.Tick += new EventHandler(OnTimer);
                }
                timer.Interval = provider.BlinkRate;
                timer.Start();
                Update(false /*timerCaused*/);
            }

            internal void StopBlinking()
            {
                if (timer != null)
                {
                    timer.Stop();
                }
                Update(false /*timerCaused*/);
            }

            /// <summary>
            ///  Move and size the error window, compute and set the window region,
            ///  set the tooltip rectangles and descriptions.  This basically brings
            ///  the error window up to date with the internal data structures.
            /// </summary>
            public void Update(bool timerCaused)
            {
                IconRegion iconRegion = provider.Region;
                Size size = iconRegion.Size;
                windowBounds = Rectangle.Empty;
                for (int i = 0; i < items.Count; i++)
                {
                    ControlItem item = (ControlItem)items[i];
                    Rectangle iconBounds = item.GetIconBounds(size);
                    if (windowBounds.IsEmpty)
                    {
                        windowBounds = iconBounds;
                    }
                    else
                    {
                        windowBounds = Rectangle.Union(windowBounds, iconBounds);
                    }
                }

                Region windowRegion = new Region(new Rectangle(0, 0, 0, 0));
                IntPtr windowRegionHandle = IntPtr.Zero;
                try
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        ControlItem item = (ControlItem)items[i];
                        Rectangle iconBounds = item.GetIconBounds(size);
                        iconBounds.X -= windowBounds.X;
                        iconBounds.Y -= windowBounds.Y;

                        bool showIcon = true;
                        if (!item.ToolTipShown)
                        {
                            switch (provider.BlinkStyle)
                            {
                                case ErrorBlinkStyle.NeverBlink:
                                    // always show icon
                                    break;

                                case ErrorBlinkStyle.BlinkIfDifferentError:
                                    showIcon = (item.BlinkPhase == 0) || (item.BlinkPhase > 0 && (item.BlinkPhase & 1) == (i & 1));
                                    break;

                                case ErrorBlinkStyle.AlwaysBlink:
                                    showIcon = ((i & 1) == 0) == provider.showIcon;
                                    break;
                            }
                        }

                        if (showIcon)
                        {
                            iconRegion.Region.Translate(iconBounds.X, iconBounds.Y);
                            windowRegion.Union(iconRegion.Region);
                            iconRegion.Region.Translate(-iconBounds.X, -iconBounds.Y);
                        }

                        if (tipWindow != null)
                        {
                            NativeMethods.TOOLINFO_T toolInfo = new NativeMethods.TOOLINFO_T();
                            toolInfo.cbSize = Marshal.SizeOf(toolInfo);
                            toolInfo.hwnd = Handle;
                            toolInfo.uId = item.Id;
                            toolInfo.lpszText = item.Error;
                            toolInfo.rect = NativeMethods.RECT.FromXYWH(iconBounds.X, iconBounds.Y, iconBounds.Width, iconBounds.Height);
                            toolInfo.uFlags = NativeMethods.TTF_SUBCLASS;
                            if (provider.RightToLeft)
                            {
                                toolInfo.uFlags |= NativeMethods.TTF_RTLREADING;
                            }
                            UnsafeNativeMethods.SendMessage(new HandleRef(tipWindow, tipWindow.Handle), NativeMethods.TTM_SETTOOLINFO, 0, toolInfo);
                        }

                        if (timerCaused && item.BlinkPhase > 0)
                        {
                            item.BlinkPhase--;
                        }
                    }
                    if (timerCaused)
                    {
                        provider.showIcon = !provider.showIcon;
                    }

                    DeviceContext dc = null;
                    dc = DeviceContext.FromHwnd(Handle);
                    try
                    {
                        CreateMirrorDC(dc.Hdc, windowBounds.Width);

                        Graphics graphics = Graphics.FromHdcInternal(mirrordc.Hdc);
                        try
                        {
                            windowRegionHandle = windowRegion.GetHrgn(graphics);
                        }
                        finally
                        {
                            graphics.Dispose();
                            RestoreMirrorDC();
                        }

                        if (UnsafeNativeMethods.SetWindowRgn(new HandleRef(this, Handle), new HandleRef(windowRegion, windowRegionHandle), true) != 0)
                        {
                            //The HWnd owns the region.
                            windowRegionHandle = IntPtr.Zero;
                        }
                    }

                    finally
                    {
                        if (dc != null)
                        {
                            dc.Dispose();
                        }
                    }

                }
                finally
                {
                    windowRegion.Dispose();
                    if (windowRegionHandle != IntPtr.Zero)
                    {
                        SafeNativeMethods.DeleteObject(new HandleRef(null, windowRegionHandle));
                    }
                }

                SafeNativeMethods.SetWindowPos(new HandleRef(this, Handle), NativeMethods.HWND_TOP, windowBounds.X, windowBounds.Y,
                                     windowBounds.Width, windowBounds.Height, NativeMethods.SWP_NOACTIVATE);
                SafeNativeMethods.InvalidateRect(new HandleRef(this, Handle), null, false);
            }

            /// <summary>
            ///  Called when the error window gets a windows message.
            /// </summary>
            protected override void WndProc(ref Message m)
            {
                switch (m.Msg)
                {
                    case Interop.WindowMessages.WM_NOTIFY:
                        NativeMethods.NMHDR nmhdr = (NativeMethods.NMHDR)m.GetLParam(typeof(NativeMethods.NMHDR));
                        if (nmhdr.code == NativeMethods.TTN_SHOW || nmhdr.code == NativeMethods.TTN_POP)
                        {
                            OnToolTipVisibilityChanging(nmhdr.idFrom, nmhdr.code == NativeMethods.TTN_SHOW);
                        }
                        break;
                    case Interop.WindowMessages.WM_ERASEBKGND:
                        break;
                    case Interop.WindowMessages.WM_PAINT:
                        OnPaint(ref m);
                        break;
                    default:
                        base.WndProc(ref m);
                        break;
                }
            }
        }

        /// <summary>
        ///  There is one ControlItem for each control that the ErrorProvider is
        ///  is tracking state for.  It contains the values of all the extender
        ///  properties.
        /// </summary>
        internal class ControlItem
        {
            //
            // FIELDS
            //

            string error;
            readonly Control control;
            ErrorWindow window;
            readonly ErrorProvider provider;
            int blinkPhase;
            readonly IntPtr id;
            int iconPadding;
            bool toolTipShown;
            ErrorIconAlignment iconAlignment;
            const int startingBlinkPhase = 10;          // cause we want to blink 5 times

            //
            // CONSTRUCTORS
            //

            /// <summary>
            ///  Construct the item with its associated control, provider, and
            ///  a unique ID.  The ID is used for the tooltip ID.
            /// </summary>
            public ControlItem(ErrorProvider provider, Control control, IntPtr id)
            {
                toolTipShown = false;
                iconAlignment = defaultIconAlignment;
                error = string.Empty;
                this.id = id;
                this.control = control;
                this.provider = provider;
                this.control.HandleCreated += new EventHandler(OnCreateHandle);
                this.control.HandleDestroyed += new EventHandler(OnDestroyHandle);
                this.control.LocationChanged += new EventHandler(OnBoundsChanged);
                this.control.SizeChanged += new EventHandler(OnBoundsChanged);
                this.control.VisibleChanged += new EventHandler(OnParentVisibleChanged);
                this.control.ParentChanged += new EventHandler(OnParentVisibleChanged);
            }

            public void Dispose()
            {
                if (control != null)
                {
                    control.HandleCreated -= new EventHandler(OnCreateHandle);
                    control.HandleDestroyed -= new EventHandler(OnDestroyHandle);
                    control.LocationChanged -= new EventHandler(OnBoundsChanged);
                    control.SizeChanged -= new EventHandler(OnBoundsChanged);
                    control.VisibleChanged -= new EventHandler(OnParentVisibleChanged);
                    control.ParentChanged -= new EventHandler(OnParentVisibleChanged);
                }
                error = string.Empty;
            }

            //
            // PROPERTIES
            //

            /// <summary>
            ///  Returns the unique ID for this control.  The ID used as the tooltip ID.
            /// </summary>
            public IntPtr Id
            {
                get
                {
                    return id;
                }
            }

            /// <summary>
            ///  Returns or set the phase of blinking that this control is currently
            ///  in.   If zero, the control is not blinking.  If odd, then the control
            ///  is blinking, but invisible.  If even, the control is blinking and
            ///  currently visible.  Each time the blink timer fires, this value is
            ///  reduced by one (until zero), thus causing the error icon to appear
            ///  or disappear.
            /// </summary>
            public int BlinkPhase
            {
                get
                {
                    return blinkPhase;
                }
                set
                {
                    blinkPhase = value;
                }
            }

            /// <summary>
            ///  Returns or sets the icon padding for the control.
            /// </summary>
            public int IconPadding
            {
                get
                {
                    return iconPadding;
                }
                set
                {
                    if (iconPadding != value)
                    {
                        iconPadding = value;
                        UpdateWindow();
                    }
                }
            }

            /// <summary>
            ///  Returns or sets the error description string for the control.
            /// </summary>
            public string Error
            {
                get
                {
                    return error;
                }
                set
                {
                    if (value == null)
                    {
                        value = string.Empty;
                    }

                    // if the error is the same and the blinkStyle is not AlwaysBlink, then
                    // we should not add the error and not start blinking.
                    if (error.Equals(value) && provider.BlinkStyle != ErrorBlinkStyle.AlwaysBlink)
                    {
                        return;
                    }

                    bool adding = error.Length == 0;
                    error = value;
                    if (value.Length == 0)
                    {
                        RemoveFromWindow();
                    }
                    else
                    {
                        if (adding)
                        {
                            AddToWindow();
                        }
                        else
                        {
                            if (provider.BlinkStyle != ErrorBlinkStyle.NeverBlink)
                            {
                                StartBlinking();
                            }
                            else
                            {
                                UpdateWindow();
                            }
                        }
                    }
                }
            }

            /// <summary>
            ///  Returns or sets the location of the error icon for the control.
            /// </summary>
            public ErrorIconAlignment IconAlignment
            {
                get
                {
                    return iconAlignment;
                }
                set
                {
                    if (iconAlignment != value)
                    {
                        //valid values are 0x0 to 0x5
                        if (!ClientUtils.IsEnumValid(value, (int)value, (int)ErrorIconAlignment.TopLeft, (int)ErrorIconAlignment.BottomRight))
                        {
                            throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ErrorIconAlignment));
                        }
                        iconAlignment = value;
                        UpdateWindow();
                    }
                }
            }

            /// <summary>
            ///  Returns true if the tooltip for this control item is currently shown.
            /// </summary>
            public bool ToolTipShown
            {
                get
                {
                    return toolTipShown;
                }
                set
                {
                    toolTipShown = value;
                }
            }

            internal ErrorIconAlignment RTLTranslateIconAlignment(ErrorIconAlignment align)
            {
                if (provider.RightToLeft)
                {
                    switch (align)
                    {
                        case ErrorIconAlignment.TopLeft:
                            return ErrorIconAlignment.TopRight;
                        case ErrorIconAlignment.MiddleLeft:
                            return ErrorIconAlignment.MiddleRight;
                        case ErrorIconAlignment.BottomLeft:
                            return ErrorIconAlignment.BottomRight;
                        case ErrorIconAlignment.TopRight:
                            return ErrorIconAlignment.TopLeft;
                        case ErrorIconAlignment.MiddleRight:
                            return ErrorIconAlignment.MiddleLeft;
                        case ErrorIconAlignment.BottomRight:
                            return ErrorIconAlignment.BottomLeft;
                        default:
                            Debug.Fail("Unknown ErrorIconAlignment value");
                            return align;
                    }
                }
                else
                {
                    return align;
                }
            }

            /// <summary>
            ///  Returns the location of the icon in the same coordinate system as
            ///  the control being extended.  The size passed in is the size of
            ///  the icon.
            /// </summary>
            internal Rectangle GetIconBounds(Size size)
            {
                int x = 0;
                int y = 0;

                switch (RTLTranslateIconAlignment(IconAlignment))
                {
                    case ErrorIconAlignment.TopLeft:
                    case ErrorIconAlignment.MiddleLeft:
                    case ErrorIconAlignment.BottomLeft:
                        x = control.Left - size.Width - iconPadding;
                        break;
                    case ErrorIconAlignment.TopRight:
                    case ErrorIconAlignment.MiddleRight:
                    case ErrorIconAlignment.BottomRight:
                        x = control.Right + iconPadding;
                        break;
                }

                switch (IconAlignment)
                {
                    case ErrorIconAlignment.TopLeft:
                    case ErrorIconAlignment.TopRight:
                        y = control.Top;
                        break;
                    case ErrorIconAlignment.MiddleLeft:
                    case ErrorIconAlignment.MiddleRight:
                        y = control.Top + (control.Height - size.Height) / 2;
                        break;
                    case ErrorIconAlignment.BottomLeft:
                    case ErrorIconAlignment.BottomRight:
                        y = control.Bottom - size.Height;
                        break;
                }

                return new Rectangle(x, y, size.Width, size.Height);
            }

            /// <summary>
            ///  If this control's error icon has been added to the error
            ///  window, then update the window state because some property
            ///  has changed.
            /// </summary>
            void UpdateWindow()
            {
                if (window != null)
                {
                    window.Update(false /*timerCaused*/);
                }
            }

            /// <summary>
            ///  If this control's error icon has been added to the error
            ///  window, then start blinking the error window.  The blink
            ///  count
            /// </summary>
            void StartBlinking()
            {
                if (window != null)
                {
                    BlinkPhase = startingBlinkPhase;
                    window.StartBlinking();
                }
            }

            /// <summary>
            ///  Add this control's error icon to the error window.
            /// </summary>
            void AddToWindow()
            {
                // if we are recreating the control, then add the control.
                if (window == null &&
                    (control.Created || control.RecreatingHandle) &&
                    control.Visible && control.ParentInternal != null &&
                    error.Length > 0)
                {
                    window = provider.EnsureErrorWindow(control.ParentInternal);
                    window.Add(this);
                    // Make sure that we blink if the style is set to AlwaysBlink or BlinkIfDifferrentError
                    if (provider.BlinkStyle != ErrorBlinkStyle.NeverBlink)
                    {
                        StartBlinking();
                    }
                }
            }

            /// <summary>
            ///  Remove this control's error icon from the error window.
            /// </summary>
            void RemoveFromWindow()
            {
                if (window != null)
                {
                    window.Remove(this);
                    window = null;
                }
            }

            /// <summary>
            ///  This is called when a property on the control is changed.
            /// </summary>
            void OnBoundsChanged(object sender, EventArgs e)
            {
                UpdateWindow();
            }

            void OnParentVisibleChanged(object sender, EventArgs e)
            {
                BlinkPhase = 0;
                RemoveFromWindow();
                AddToWindow();
            }

            /// <summary>
            ///  This is called when the control's handle is created.
            /// </summary>
            void OnCreateHandle(object sender, EventArgs e)
            {
                AddToWindow();
            }

            /// <summary>
            ///  This is called when the control's handle is destroyed.
            /// </summary>
            void OnDestroyHandle(object sender, EventArgs e)
            {
                RemoveFromWindow();
            }
        }

        /// <summary>
        ///  This represents the HRGN of icon.  The region is calculate from the icon's mask.
        /// </summary>
        internal class IconRegion
        {
            //
            // FIELDS
            //

            Region region;
            readonly Icon icon;

            //
            // CONSTRUCTORS
            //

            /// <summary>
            ///  Constructor that takes an Icon and extracts its 16x16 version.
            /// </summary>
            public IconRegion(Icon icon)
            {
                this.icon = new Icon(icon, 16, 16);
            }

            //
            // PROPERTIES
            //

            /// <summary>
            ///  Returns the handle of the icon.
            /// </summary>
            public IntPtr IconHandle
            {
                get
                {
                    return icon.Handle;
                }
            }

            /// <summary>
            ///  Returns the handle of the region.
            /// </summary>
            public Region Region
            {

                get
                {
                    if (region == null)
                    {
                        region = new Region(new Rectangle(0, 0, 0, 0));

                        IntPtr mask = IntPtr.Zero;
                        try
                        {
                            Size size = icon.Size;
                            Bitmap bitmap = icon.ToBitmap();
                            mask = ControlPaint.CreateHBitmapTransparencyMask(bitmap);
                            bitmap.Dispose();

                            // It is been observed that users can use non standard size icons (not a 16 bit multiples for width and height)
                            // and GetBitmapBits method allocate bytes in multiple of 16 bits for each row. Following calculation is to get right width in bytes.
                            int bitmapBitsAllocationSize = 16;

                            //if width is not multiple of 16, we need to allocate BitmapBitsAllocationSize for remaining bits.
                            int widthInBytes = 2 * ((size.Width + 15) / bitmapBitsAllocationSize); // its in bytes.
                            byte[] bits = new byte[widthInBytes * size.Height];
                            SafeNativeMethods.GetBitmapBits(new HandleRef(null, mask), bits.Length, bits);

                            for (int y = 0; y < size.Height; y++)
                            {
                                for (int x = 0; x < size.Width; x++)
                                {

                                    // see if bit is set in mask.  bits in byte are reversed. 0 is black (set).
                                    if ((bits[y * widthInBytes + x / 8] & (1 << (7 - (x % 8)))) == 0)
                                    {
                                        region.Union(new Rectangle(x, y, 1, 1));
                                    }
                                }
                            }
                            region.Intersect(new Rectangle(0, 0, size.Width, size.Height));
                        }
                        finally
                        {
                            if (mask != IntPtr.Zero)
                            {
                                SafeNativeMethods.DeleteObject(new HandleRef(null, mask));
                            }
                        }
                    }

                    return region;
                }
            }

            /// <summary>
            ///  Return the size of the icon.
            /// </summary>
            public Size Size
            {
                get
                {
                    return icon.Size;
                }
            }

            //
            // METHODS
            //

            /// <summary>
            ///  Release any resources held by this Object.
            /// </summary>
            public void Dispose()
            {
                if (region != null)
                {
                    region.Dispose();
                    region = null;
                }
                icon.Dispose();
            }

        }
    }
}


