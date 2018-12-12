﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Foundation;
using CoreGraphics;

#if __IOS__
using UIKit;
using NativeTextView = UIKit.UITextView;
using INativeTextViewDelegate = UIKit.IUITextViewDelegate;
using NativeView = UIKit.UIView;
using NativeColor = UIKit.UIColor;
using NativeFont = UIKit.UIFont;
using NativeStringAttributes = UIKit.UIStringAttributes;
#elif __MACOS__
using AppKit;
using NativeTextView = AppKit.NSTextView;
using INativeTextViewDelegate = AppKit.INSTextViewDelegate;
using NativeView = AppKit.NSView;
using NativeColor = AppKit.NSColor;
using NativeFont = AppKit.NSFont;
using NativeStringAttributes = AppKit.NSStringAttributes;
#endif

using CLanguage.Compiler;
using CLanguage.Syntax;
using static CLanguage.Editor.Extensions;

namespace CLanguage.Editor
{
    [Register ("CEditor")]
    public class CEditor : NativeView, INSTextStorageDelegate, INativeTextViewDelegate
    {
        readonly EditorTextView textView;
        public NativeTextView TextView => textView;

        readonly ErrorView errorView = new ErrorView () { AlphaValue = 0 };
        nfloat errorHeight = (nfloat)32;
        nfloat errorHMargin = (nfloat)18;
        nfloat errorVMargin = (nfloat)16;
        NSLayoutConstraint errorBottomConstraint;

        readonly MarginView margin = new MarginView ();
        nfloat marginWidth = (nfloat)36;
        NSLayoutConstraint marginWidthConstraint;

        public string Text {
            get => textView.TextStorage.Value;
            set {
                value = value ?? "";
                var oldText = textView.TextStorage.Value;
                if (oldText == value)
                    return;
                textView.TextStorage.SetString (new NSAttributedString (value ?? "", theme.CommentAttributes));
                BeginInvokeOnMainThread (() => {
                    ColorizeCode (textView.TextStorage);
                    UpdateMargin ();
                    if (oldText.Length == 0 && value.Length > 0) {
                        textView.SelectedRange = new NSRange (0, 0);
                    }
                    NeedsLayout = true;
                });
            }
        }

        public NSRange SelectedRange {
            get => textView.SelectedRange;
            set => textView.SelectedRange = value;
        }

        CompilerOptions options = new CompilerOptions (new MachineInfo (), new Report (), Enumerable.Empty<Document> ());
        public CompilerOptions Options {
            get => options;
            set {
                options = value;
                ColorizeCode (textView.TextStorage);
            }
        }

        Theme theme;
        public Theme Theme {
            get => theme;
            set {
                theme = value;
                OnThemeChanged ();
            }
        }

        List<int> lineStarts = new List<int> (1) { 0 };

        public event EventHandler TextChanged;

#if __IOS__
        NativeColor EffectiveAppearance => TintColor;
        static bool IsDark (NativeColor a) => true;
        bool NeedsLayout { get => false; set => SetNeedsLayout (); }
        static readonly bool ios11 = UIDevice.CurrentDevice.CheckSystemVersion (11, 0);
#elif __MACOS__
        readonly NSScrollView scroll;
        IDisposable scrolledSubscription;
        IDisposable appearanceObserver;
        static bool IsDark (NSAppearance a) => a.Name.Contains ("dark", StringComparison.InvariantCultureIgnoreCase);
#endif

        public CEditor (NSCoder coder) : base (coder)
        {
            textView = new EditorTextView (Bounds);
#if __MACOS__
            scroll = new NSScrollView (Bounds);
#elif __IOS__
#endif
            theme = new Theme (IsDark (EffectiveAppearance));
            Initialize ();
        }

        public CEditor (IntPtr handle) : base (handle)
        {
            textView = new EditorTextView (Bounds);
#if __MACOS__
            scroll = new NSScrollView (Bounds);
#elif __IOS__
#endif
            theme = new Theme (IsDark (EffectiveAppearance));
            Initialize ();
        }

        public CEditor (CGRect frameRect) : base (frameRect)
        {
            textView = new EditorTextView (Bounds);
#if __MACOS__
            scroll = new NSScrollView (Bounds);
#elif __IOS__
#endif
            theme = new Theme (IsDark (EffectiveAppearance));
            Initialize ();
        }

        void Initialize ()
        {
            var sframe = Bounds;
            var mframe = sframe;
            var eframe = sframe;
            mframe.Width = marginWidth;
            sframe.X += marginWidth;
            sframe.Width -= marginWidth;
            eframe.Height = errorHeight;

            textView.Font = theme.CodeFont;
            textView.TypingAttributes = theme.TypingAttributes;

            textView.Delegate = this;
            textView.TextStorage.Delegate = this;

#if __MACOS__
            textView.MaxSize = new CGSize (nfloat.MaxValue, nfloat.MaxValue);
            textView.VerticallyResizable = true;
            textView.HorizontallyResizable = true;
            textView.AutoresizingMask = NSViewResizingMask.WidthSizable;
            textView.TranslatesAutoresizingMaskIntoConstraints = true;
            textView.AutomaticTextReplacementEnabled = false;
            textView.AutomaticDashSubstitutionEnabled = false;
            textView.AutomaticQuoteSubstitutionEnabled = false;
            textView.AutomaticSpellingCorrectionEnabled = false;
            textView.SmartInsertDeleteEnabled = false;
            textView.TextContainer.ContainerSize = new CGSize (nfloat.MaxValue, nfloat.MaxValue);
            textView.TextContainer.WidthTracksTextView = false;
            textView.TextContainer.LineBreakMode = NSLineBreakMode.Clipping;
            textView.AllowsUndo = true;
            textView.SelectedTextAttributes = theme.SelectedAttributes;
            NSUserDefaults.StandardUserDefaults.SetInt (50, "NSInitialToolTipDelay");
            textView.DisplaysLinkToolTips = true;

            scroll.VerticalScrollElasticity = NSScrollElasticity.Allowed;
            scroll.HorizontalScrollElasticity = NSScrollElasticity.Allowed;
            scroll.HasVerticalScroller = true;
            scroll.HasHorizontalScroller = true;
            scroll.DocumentView = textView;
            scroll.BackgroundColor = textView.BackgroundColor;
            scroll.DrawsBackground = true;
            scroll.AutomaticallyAdjustsContentInsets = true;

            scroll.ContentView.PostsBoundsChangedNotifications = true;
            scrolledSubscription = NativeView.Notifications.ObserveBoundsChanged (scroll.ContentView, (sender, e) => {
                UpdateMargin ();
            });

            appearanceObserver = this.AddObserver ("effectiveAppearance", NSKeyValueObservingOptions.Initial | NSKeyValueObservingOptions.New, change => {
                if (change.NewValue is NSAppearance a) {
                    Theme = new Theme (isDark: IsDark (a));
                }
            });
#elif __IOS__
            var scroll = textView;
            textView.AlwaysBounceVertical = true;
            textView.AlwaysBounceHorizontal = false;
            textView.InputAssistantItem.LeadingBarButtonGroups = null;
            textView.InputAssistantItem.TrailingBarButtonGroups = null;
            textView.AutocorrectionType = UITextAutocorrectionType.No;
            textView.AutocapitalizationType = UITextAutocapitalizationType.None;
            textView.AllowsEditingTextAttributes = false;
            textView.KeyboardType = UIKeyboardType.Default;
            if (ios11) {
                textView.SmartInsertDeleteType = UITextSmartInsertDeleteType.No;
                textView.SmartDashesType = UITextSmartDashesType.No;
                textView.SmartQuotesType = UITextSmartQuotesType.No;
                errorVMargin = 0; // Safe area insets are used instead
            }
#endif

            scroll.Frame = sframe;
            margin.Frame = mframe;
            errorView.Frame = eframe;

            scroll.TranslatesAutoresizingMaskIntoConstraints = false;
            margin.TranslatesAutoresizingMaskIntoConstraints = false;
            errorView.TranslatesAutoresizingMaskIntoConstraints = false;

            AddSubview (scroll);
            AddSubview (margin);
            AddSubview (errorView);

            AddConstraint (NSLayoutConstraint.Create (margin, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, scroll, NSLayoutAttribute.Leading, 1, 0));
            AddConstraint (NSLayoutConstraint.Create (this, NSLayoutAttribute.Top, NSLayoutRelation.Equal, scroll, NSLayoutAttribute.Top, 1, 0));
            AddConstraint (NSLayoutConstraint.Create (this, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, scroll, NSLayoutAttribute.Bottom, 1, 0));

            AddConstraint (marginWidthConstraint = NSLayoutConstraint.Create (margin, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, marginWidth));
            AddConstraint (NSLayoutConstraint.Create (margin, NSLayoutAttribute.Top, NSLayoutRelation.Equal, scroll, NSLayoutAttribute.Top, 1, 0));
            AddConstraint (NSLayoutConstraint.Create (margin, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, scroll, NSLayoutAttribute.Bottom, 1, 0));

            AddConstraint (NSLayoutConstraint.Create (this, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, errorView, NSLayoutAttribute.Leading, 1, -errorHMargin));
            AddConstraint (NSLayoutConstraint.Create (errorView, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, errorHeight));

#if __MACOS__
            AddConstraint (NSLayoutConstraint.Create (this, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, scroll, NSLayoutAttribute.Trailing, 1, 0));
            AddConstraint (errorBottomConstraint = NSLayoutConstraint.Create (this, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, errorView, NSLayoutAttribute.Bottom, 1, errorVMargin));
            AddConstraint (NSLayoutConstraint.Create (this, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, margin, NSLayoutAttribute.Leading, 1, 0));
            AddConstraint (NSLayoutConstraint.Create (this, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, errorView, NSLayoutAttribute.Trailing, 1, errorHMargin));
#elif __IOS__
            if (ios11) {
                AddConstraint (NSLayoutConstraint.Create (SafeAreaLayoutGuide, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, scroll, NSLayoutAttribute.Trailing, 1, 0));
                AddConstraint (NSLayoutConstraint.Create (SafeAreaLayoutGuide, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, margin, NSLayoutAttribute.Leading, 1, 0));
                AddConstraint (errorBottomConstraint = NSLayoutConstraint.Create (SafeAreaLayoutGuide, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, errorView, NSLayoutAttribute.Bottom, 1, errorVMargin));
                AddConstraint (NSLayoutConstraint.Create (SafeAreaLayoutGuide, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, errorView, NSLayoutAttribute.Trailing, 1, errorHMargin));
            }
            else {
                AddConstraint (NSLayoutConstraint.Create (this, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, scroll, NSLayoutAttribute.Trailing, 1, 0));
                AddConstraint (NSLayoutConstraint.Create (this, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, margin, NSLayoutAttribute.Leading, 1, 0));
                AddConstraint (errorBottomConstraint = NSLayoutConstraint.Create (this, NSLayoutAttribute.Bottom, NSLayoutRelation.Equal, errorView, NSLayoutAttribute.Bottom, 1, errorVMargin));
                AddConstraint (NSLayoutConstraint.Create (this, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, errorView, NSLayoutAttribute.Trailing, 1, errorHMargin));
            }
#endif

            OnThemeChanged ();
        }

        [Export ("toggleComment:")]
        public void ToggleComment (NSObject sender)
        {
            textView.ChangeSelectedLines (ToggleCommentMapper);
        }

        IEnumerable<string> ToggleCommentMapper (string code, NSRange range, List<string> lines)
        {
            var line0T = lines[0].TrimStart ();
            var remove = line0T.Length > 1 && line0T[0] == '/' && line0T[1] == '/';
            foreach (var line in lines) {
                var indent = line.GetIndent ();
                var nline = indent;
                var s = indent.Length;
                if (remove) {
                    var hasComment = s + 1 < line.Length && line[s] == '/' && line[s] == '/';
                    if (hasComment) {
                        nline = indent + line.Substring (s + 2);
                    }
                }
                else {
                    nline = indent + "//" + line.Substring (s);
                }
                yield return nline;
            }
        }

        [Export ("indent:")]
        public void Indent (NSObject sender)
        {
            textView.ChangeSelectedLines (IndentMapper);
        }

        IEnumerable<string> IndentMapper (string code, NSRange range, List<string> lines)
        {
            return from line in lines
                   let indent = line.GetIndent ()
                   select indent + "  " + line.Substring (indent.Length);
        }

        [Export ("outdent:")]
        public void Outdent (NSObject sender)
        {
            textView.ChangeSelectedLines (OutdentMapper);
        }

        List<string> OutdentMapper (string code, NSRange range, List<string> lines)
        {
            var r = new List<string> (lines.Count);
            foreach (var line in lines) {
                var indent = line.GetIndent ();
                if (indent.Length == 0) {
                    r.Add (line);
                }
                else if (indent.Length == 1) {
                    r.Add (line.Substring (1));
                }
                else {
                    var newIndent = indent.Substring (0, indent.Length - 2);
                    r.Add (newIndent + line.Substring (indent.Length));
                }
            }
            return r;
        }

        [Export ("textStorage:didProcessEditing:range:changeInLength:")]
        async void DidProcessEditing (NSTextStorage textStorage, NSTextStorageEditActions editedMask, NSRange editedRange, nint delta)
        {
            if (editedMask.HasFlag (NSTextStorageEditActions.Characters)) {
                //
                // Have to yield here because this is called *before* the layout managers are updated.
                // And we need them to be in sync. So we yield and catch the next run loop.
                //
                await Task.Yield ();

                ColorizeCode (textStorage);
                UpdateMargin ();
                TextChanged?.Invoke (this, EventArgs.Empty);
            }
        }

#if __IOS__
        [Export ("scrollViewDidScroll:")]
        public void Scrolled (UIScrollView scrollView)
        {
            UpdateMargin ();
        }
#endif

        void UpdateMargin ()
        {
            var layoutManager = textView.LayoutManager;
            var textContainer = textView.TextContainer;
#if __MACOS__
            var bounds = scroll.ContentView.Bounds;
            var lfpad = textView.TextContainerInset.Height;
#elif __IOS__
            var bounds = textView.Bounds;
            var lfpad = textView.TextContainerInset.Top;
#endif
            var visibleGlyphs = layoutManager.GlyphRangeForBoundingRect (bounds, textContainer);
            var visibleChars = layoutManager.CharacterRangeForGlyphRange (visibleGlyphs);
            var lines = textView.GetLinesInRange (visibleChars);
            var index = lines.Range.Location;
            var lineBounds = new List<CGRect> (lines.Lines.Count);
            for (var i = 0; i < lines.Lines.Count && index <= lines.AllText.Length; i++) {
                var line = lines.Lines[i];
                var cr = new NSRange (index, line.Length);
                var gr = layoutManager.GlyphRangeForCharacterRange (cr);
                var b = layoutManager.BoundingRectForGlyphRange (gr, textContainer);
                b.Y += lfpad;
                lineBounds.Add (b);
                index += line.Length + 1;
            }

            margin.SetLinePositions ((int)lines.Range.Location, lineBounds, bounds, lineStarts);
        }


        void EnumerateLineFragments (CGRect rect, CGRect usedRectangle, NSTextContainer textContainer, NSRange glyphRange, ref bool stop)
        {
            Console.WriteLine ($"LINE {rect} --> {usedRectangle}");
        }

        static readonly char[] newlineChars = { '\n', (char)8232 };

        void ColorizeCode (NSTextStorage textStorage)
        {
            var code = textStorage.Value;
            var managers = textStorage.LayoutManagers;

            //
            // Count the lines
            //
            var lineStarts = new List<int> (code.Length / 20) { 0 };
            var lc = 1;
            var li = code.IndexOfAny (newlineChars);
            while (li >= 0) {
                if (li + 1 <= code.Length)
                    lineStarts.Add (li + 1);
                lc++;
                li = li + 1 < code.Length ? code.IndexOfAny (newlineChars, li + 1) : -1;
            }
            Debug.Assert (lc == lineStarts.Count, $"Line count mismatch: {lc} != {lineStarts.Count}");
            this.lineStarts = lineStarts;

#if __MACOS__
            //
            // Use the language service to determine colors and errors
            //
            var printer = new EditorPrinter ();
            var spans = CLanguage.CLanguageService.Colorize (code, options.MachineInfo, printer);

            //
            // Color the text
            //
            foreach (var lm in managers) {
                lm.RemoveTemporaryAttribute (NSStringAttributeKey.ForegroundColor, new NSRange (0, code.Length));
                lm.RemoveTemporaryAttribute (NSStringAttributeKey.BackgroundColor, new NSRange (0, code.Length));
                lm.RemoveTemporaryAttribute (NSStringAttributeKey.ToolTip, new NSRange (0, code.Length));
                lm.RemoveTemporaryAttribute (NSStringAttributeKey.UnderlineStyle, new NSRange (0, code.Length));
            }
            var colorAttrs = theme.ColorAttributes;
            foreach (var s in spans) {
                var attrs = colorAttrs[(int)s.Color];
                var range = new NSRange (s.Index, s.Length);
                foreach (var lm in managers) {
                    lm.SetTemporaryAttributes (attrs, range);
                }
            }

            //
            // Mark errors
            //
            foreach (var m in printer.Messages) {
                if (m.Location.IsNull || m.EndLocation.IsNull)
                    continue;
                if (m.Location.Document.Path != CLanguageService.DefaultCodePath)
                    continue;
                var range = new NSRange (m.Location.Index, m.EndLocation.Index - m.Location.Index);
                if (range.Location >= 0 && range.Length > 0 && range.Location < code.Length && range.Location + range.Length <= code.Length) {
                    var attrs = m.MessageType == "Error" ? theme.ErrorAttributes (m.Text, null) : theme.WarningAttributes (m.Text, null);
                    foreach (var lm in managers) {
                        lm.AddTemporaryAttributes (attrs, range);
                    }
                }
            }
#elif __IOS__
            var ts = (EditorTextStorage)textView.TextStorage;
            ts.Options = options;
            ts.Theme = Theme;
            var printer = ts.LastPrinter;
#endif

            //
            // Inform the error view
            //
            errorView.Message = printer.Messages.FirstOrDefault (x => x.MessageType == "Error") ?? new Report.AbstractMessage ("Info", "");
        }

        void OnThemeChanged ()
        {
            margin.Theme = theme;
            errorView.Theme = theme;
            textView.BackgroundColor = theme.BackgroundColor;
            ColorizeCode (textView.TextStorage);
#if __MACOS__
            textView.SelectedTextAttributes = theme.SelectedAttributes;
            scroll.DrawsBackground = true;
            scroll.BackgroundColor = textView.BackgroundColor;
#elif __IOS__
            ((EditorTextStorage)textView.TextStorage).Theme = theme;
            BackgroundColor = theme.BackgroundColor;
            textView.KeyboardAppearance = theme.IsDark ? UIKeyboardAppearance.Dark : UIKeyboardAppearance.Light;
#endif
            SetNeedsDisplayInRect (Bounds);
        }
    }
}
