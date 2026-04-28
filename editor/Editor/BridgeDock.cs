using Editor;
using Sandbox;

namespace SboxAgentBridge.Editor;

[Dock( "Editor", "Agent Bridge", "hub" )]
public sealed class BridgeDock : Widget
{
	private readonly Label _statusLabel;
	private readonly Label _ipcLabel;

	public BridgeDock( Widget parent ) : base( parent, false )
	{
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 6;

		SetStyles( "background-color: #202124; color: #f1f3f4;" );

		Layout.Add( new Label( "sbox Agent Bridge", this ) );

		_statusLabel = Layout.Add( new Label( "Status: starting", this ) );
		_ipcLabel = Layout.Add( new Label( $"IPC: {BridgeRuntime.IpcRoot}", this ) );
		_ipcLabel.WordWrap = true;

		var startButton = Layout.Add( new Button( "Start Bridge", this ) );
		startButton.Clicked += () =>
		{
			BridgeRuntime.Start();
			RefreshStatus();
		};

		var stopButton = Layout.Add( new Button( "Stop Bridge", this ) );
		stopButton.Clicked += () =>
		{
			BridgeRuntime.Stop();
			RefreshStatus();
		};

		BridgeRuntime.Start();
		RefreshStatus();
	}

	[Event( "tool.frame" )]
	public static void OnToolFrame()
	{
		BridgeRuntime.Pump();
	}

	private void RefreshStatus()
	{
		_statusLabel.Text = BridgeRuntime.IsRunning ? "Status: running" : "Status: stopped";
		_ipcLabel.Text = $"IPC: {BridgeRuntime.IpcRoot}";
	}
}
