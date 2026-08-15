using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal static class TrayControllerTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("tray menu has the exact command order", delegate {
            FakeTrayViewFactory factory = new FakeTrayViewFactory();
            using (TrayController tray = new TrayController("app.ico", factory))
            {
                AssertEx.Equal(5, factory.Entries.Length);
                AssertEntry(factory.Entries[0], "Open", TrayCommand.Open, false);
                AssertEntry(factory.Entries[1], "Restart Service", TrayCommand.RestartService, false);
                AssertEntry(factory.Entries[2], "View Logs", TrayCommand.ViewLogs, false);
                AssertEntry(factory.Entries[3], null, TrayCommand.Separator, true);
                AssertEntry(factory.Entries[4], "Exit", TrayCommand.Exit, false);
            }
        });

        runner.Add("tray double click maps to Open", delegate {
            FakeTrayViewFactory factory = new FakeTrayViewFactory();
            using (TrayController tray = new TrayController("app.ico", factory))
            {
                int opens = 0;
                tray.OpenRequested += delegate { opens++; };
                factory.View.RaiseDoubleClick();
                AssertEx.Equal(1, opens);
            }
        });

        runner.Add("tray command events map to application actions", delegate {
            FakeTrayViewFactory factory = new FakeTrayViewFactory();
            using (TrayController tray = new TrayController("app.ico", factory))
            {
                int restarts = 0;
                int logs = 0;
                int exits = 0;
                tray.RestartServiceRequested += delegate { restarts++; };
                tray.ViewLogsRequested += delegate { logs++; };
                tray.ExitRequested += delegate { exits++; };
                factory.View.RaiseCommand(TrayCommand.RestartService);
                factory.View.RaiseCommand(TrayCommand.ViewLogs);
                factory.View.RaiseCommand(TrayCommand.Exit);
                AssertEx.Equal(1, restarts);
                AssertEx.Equal(1, logs);
                AssertEx.Equal(1, exits);
            }
        });

        runner.Add("tray disposal hides and disposes its platform icon", delegate {
            FakeTrayViewFactory factory = new FakeTrayViewFactory();
            TrayController tray = new TrayController("app.ico", factory);

            tray.Dispose();
            tray.Dispose();

            AssertEx.Equal(1, factory.View.DisposeCalls);
        });

        runner.Add("native tray construction disposes the claimed icon when a property setter throws", delegate {
            ThrowingNativeNotifyIconFactory nativeFactory = new ThrowingNativeNotifyIconFactory();
            string iconPath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "assets", "branding", "dsh-whale.ico"));
            TrayMenuEntry[] entries = new[]
            {
                new TrayMenuEntry("Open", TrayCommand.Open, false)
            };
            bool threwExpected = false;

            try { new NotifyIconTrayView(iconPath, entries, nativeFactory); }
            catch (InvalidOperationException exception)
            {
                threwExpected = exception.Message == "native text setter failed";
            }

            AssertEx.True(threwExpected);
            AssertEx.Equal(1, nativeFactory.CreateCalls);
            AssertEx.Equal(1, nativeFactory.Icon.DisposeCalls);
        });
    }

    private static void AssertEntry(TrayMenuEntry entry, string text, TrayCommand command, bool separator)
    {
        AssertEx.Equal(text, entry.Text);
        AssertEx.Equal(command, entry.Command);
        AssertEx.Equal(separator, entry.IsSeparator);
    }

    private sealed class FakeTrayViewFactory : ITrayIconViewFactory
    {
        internal TrayMenuEntry[] Entries;
        internal readonly FakeTrayView View = new FakeTrayView();
        public ITrayIconView Create(string iconPath, TrayMenuEntry[] entries)
        {
            AssertEx.Equal("app.ico", iconPath);
            Entries = entries;
            return View;
        }
    }

    private sealed class FakeTrayView : ITrayIconView
    {
        public event EventHandler DoubleClicked;
        public event EventHandler<TrayCommandEventArgs> CommandInvoked;
        internal int DisposeCalls;
        public void SetRestartEnabled(bool enabled) { }
        public void Dispose() { DisposeCalls++; }
        internal void RaiseDoubleClick()
        {
            EventHandler handler = DoubleClicked;
            if (handler != null) handler(this, EventArgs.Empty);
        }
        internal void RaiseCommand(TrayCommand command)
        {
            EventHandler<TrayCommandEventArgs> handler = CommandInvoked;
            if (handler != null) handler(this, new TrayCommandEventArgs(command));
        }
    }

    private sealed class ThrowingNativeNotifyIconFactory : INativeNotifyIconFactory
    {
        internal readonly ThrowingNativeNotifyIcon Icon = new ThrowingNativeNotifyIcon();
        internal int CreateCalls;
        public INativeNotifyIcon Create()
        {
            CreateCalls++;
            return Icon;
        }
    }

    private sealed class ThrowingNativeNotifyIcon : INativeNotifyIcon
    {
        private EventHandler doubleClick;
        internal int DisposeCalls;
        public Icon Icon { set { } }
        public string Text { set { throw new InvalidOperationException("native text setter failed"); } }
        public ContextMenuStrip ContextMenuStrip { set { } }
        public bool Visible { set { } }
        public event EventHandler DoubleClick
        {
            add { doubleClick += value; }
            remove { doubleClick -= value; }
        }
        public void Dispose()
        {
            DisposeCalls++;
            doubleClick = null;
        }
    }
}
