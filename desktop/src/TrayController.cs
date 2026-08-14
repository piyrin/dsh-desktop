using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

internal enum TrayCommand
{
    Open,
    RestartService,
    ViewLogs,
    Separator,
    Exit
}

internal sealed class TrayMenuEntry
{
    internal string Text { get; private set; }
    internal TrayCommand Command { get; private set; }
    internal bool IsSeparator { get; private set; }

    internal TrayMenuEntry(string text, TrayCommand command, bool isSeparator)
    {
        Text = text;
        Command = command;
        IsSeparator = isSeparator;
    }
}

internal sealed class TrayCommandEventArgs : EventArgs
{
    internal TrayCommand Command { get; private set; }

    internal TrayCommandEventArgs(TrayCommand command)
    {
        Command = command;
    }
}

internal interface ITrayIconView : IDisposable
{
    event EventHandler DoubleClicked;
    event EventHandler<TrayCommandEventArgs> CommandInvoked;
    void SetRestartEnabled(bool enabled);
}

internal interface ITrayIconViewFactory
{
    ITrayIconView Create(string iconPath, TrayMenuEntry[] entries);
}

internal interface IDesktopTray : IDisposable
{
    event EventHandler OpenRequested;
    event EventHandler RestartServiceRequested;
    event EventHandler ViewLogsRequested;
    event EventHandler ExitRequested;
    void SetRestartEnabled(bool enabled);
}

internal sealed class TrayController : IDesktopTray
{
    private readonly ITrayIconView view;
    private bool disposed;

    internal TrayController(string iconPath)
        : this(iconPath, new NotifyIconTrayViewFactory())
    {
    }

    internal TrayController(string iconPath, ITrayIconViewFactory factory)
    {
        if (String.IsNullOrWhiteSpace(iconPath))
            throw new ArgumentException("Tray icon path is required.", "iconPath");
        if (factory == null) throw new ArgumentNullException("factory");

        TrayMenuEntry[] entries = new[]
        {
            new TrayMenuEntry("Open", TrayCommand.Open, false),
            new TrayMenuEntry("Restart Service", TrayCommand.RestartService, false),
            new TrayMenuEntry("View Logs", TrayCommand.ViewLogs, false),
            new TrayMenuEntry(null, TrayCommand.Separator, true),
            new TrayMenuEntry("Exit", TrayCommand.Exit, false)
        };
        view = factory.Create(iconPath, entries);
        if (view == null) throw new InvalidOperationException("Tray icon factory returned no view.");
        view.DoubleClicked += OnDoubleClicked;
        view.CommandInvoked += OnCommandInvoked;
    }

    public event EventHandler OpenRequested;
    public event EventHandler RestartServiceRequested;
    public event EventHandler ViewLogsRequested;
    public event EventHandler ExitRequested;

    public void SetRestartEnabled(bool enabled)
    {
        if (!disposed) view.SetRestartEnabled(enabled);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        view.DoubleClicked -= OnDoubleClicked;
        view.CommandInvoked -= OnCommandInvoked;
        view.Dispose();
    }

    private void OnDoubleClicked(object sender, EventArgs eventArgs)
    {
        Raise(OpenRequested);
    }

    private void OnCommandInvoked(object sender, TrayCommandEventArgs eventArgs)
    {
        if (eventArgs == null) return;
        if (eventArgs.Command == TrayCommand.Open) Raise(OpenRequested);
        else if (eventArgs.Command == TrayCommand.RestartService) Raise(RestartServiceRequested);
        else if (eventArgs.Command == TrayCommand.ViewLogs) Raise(ViewLogsRequested);
        else if (eventArgs.Command == TrayCommand.Exit) Raise(ExitRequested);
    }

    private void Raise(EventHandler handler)
    {
        if (!disposed && handler != null) handler(this, EventArgs.Empty);
    }
}

internal sealed class NotifyIconTrayViewFactory : ITrayIconViewFactory
{
    public ITrayIconView Create(string iconPath, TrayMenuEntry[] entries)
    {
        return new NotifyIconTrayView(iconPath, entries);
    }
}

internal interface INativeNotifyIcon : IDisposable
{
    Icon Icon { set; }
    string Text { set; }
    ContextMenuStrip ContextMenuStrip { set; }
    bool Visible { set; }
    event EventHandler DoubleClick;
}

internal interface INativeNotifyIconFactory
{
    INativeNotifyIcon Create();
}

internal sealed class NativeNotifyIconFactory : INativeNotifyIconFactory
{
    public INativeNotifyIcon Create()
    {
        return new NativeNotifyIcon();
    }
}

internal sealed class NativeNotifyIcon : INativeNotifyIcon
{
    private readonly NotifyIcon notifyIcon = new NotifyIcon();

    public Icon Icon { set { notifyIcon.Icon = value; } }
    public string Text { set { notifyIcon.Text = value; } }
    public ContextMenuStrip ContextMenuStrip { set { notifyIcon.ContextMenuStrip = value; } }
    public bool Visible { set { notifyIcon.Visible = value; } }
    public event EventHandler DoubleClick
    {
        add { notifyIcon.DoubleClick += value; }
        remove { notifyIcon.DoubleClick -= value; }
    }
    public void Dispose() { notifyIcon.Dispose(); }
}

internal sealed class NotifyIconTrayView : ITrayIconView
{
    private readonly Dictionary<TrayCommand, ToolStripMenuItem> commandItems =
        new Dictionary<TrayCommand, ToolStripMenuItem>();
    private Icon icon;
    private ContextMenuStrip menu;
    private INativeNotifyIcon notifyIcon;
    private bool disposed;

    internal NotifyIconTrayView(string iconPath, TrayMenuEntry[] entries)
        : this(iconPath, entries, new NativeNotifyIconFactory())
    {
    }

    internal NotifyIconTrayView(
        string iconPath,
        TrayMenuEntry[] entries,
        INativeNotifyIconFactory nativeFactory)
    {
        if (entries == null) throw new ArgumentNullException("entries");
        if (nativeFactory == null) throw new ArgumentNullException("nativeFactory");
        try
        {
            icon = new Icon(iconPath);
            menu = new ContextMenuStrip();
            foreach (TrayMenuEntry entry in entries)
            {
                if (entry.IsSeparator)
                {
                    menu.Items.Add(new ToolStripSeparator());
                    continue;
                }

                ToolStripMenuItem item = new ToolStripMenuItem(entry.Text);
                TrayCommand command = entry.Command;
                item.Click += delegate { RaiseCommand(command); };
                commandItems.Add(command, item);
                menu.Items.Add(item);
            }

            notifyIcon = nativeFactory.Create();
            if (notifyIcon == null)
                throw new InvalidOperationException("Native tray icon factory returned no icon.");
            notifyIcon.Icon = icon;
            notifyIcon.Text = AppAssets.ApplicationTitle;
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.Visible = true;
            notifyIcon.DoubleClick += OnDoubleClick;
        }
        catch
        {
            try { Dispose(); }
            catch (Exception) { }
            throw;
        }
    }

    public event EventHandler DoubleClicked;
    public event EventHandler<TrayCommandEventArgs> CommandInvoked;

    public void SetRestartEnabled(bool enabled)
    {
        ToolStripMenuItem restart;
        if (!disposed && commandItems.TryGetValue(TrayCommand.RestartService, out restart))
            restart.Enabled = enabled;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Exception failure = null;
        if (notifyIcon != null)
        {
            try { notifyIcon.Visible = false; }
            catch (Exception exception) { failure = exception; }
            try { notifyIcon.DoubleClick -= OnDoubleClick; }
            catch (Exception exception) { if (failure == null) failure = exception; }
            try { notifyIcon.ContextMenuStrip = null; }
            catch (Exception exception) { if (failure == null) failure = exception; }
            try { notifyIcon.Dispose(); }
            catch (Exception exception) { if (failure == null) failure = exception; }
            finally { notifyIcon = null; }
        }
        if (menu != null)
        {
            try { menu.Dispose(); }
            catch (Exception exception) { if (failure == null) failure = exception; }
            finally { menu = null; }
        }
        if (icon != null)
        {
            try { icon.Dispose(); }
            catch (Exception exception) { if (failure == null) failure = exception; }
            finally { icon = null; }
        }
        if (failure != null) throw failure;
    }

    private void OnDoubleClick(object sender, EventArgs eventArgs)
    {
        EventHandler handler = DoubleClicked;
        if (!disposed && handler != null) handler(this, EventArgs.Empty);
    }

    private void RaiseCommand(TrayCommand command)
    {
        EventHandler<TrayCommandEventArgs> handler = CommandInvoked;
        if (!disposed && handler != null) handler(this, new TrayCommandEventArgs(command));
    }
}
