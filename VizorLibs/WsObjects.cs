using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Net.WebSockets;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Reactive.Linq;


using Websocket.Client;
/// <summary>
///  Websocket implementation based on the open source work of Bengesht
/// </summary>

namespace VizorLibs
{
	public class WsObject
	{
		private WebsocketClient webSocket;
		private Uri uri;
		public ConnectionStatus status;
		public string message;
		private string initMessage;
		public event EventHandler changed;
		public event EventHandler statusChanged;

		public enum ConnectionStatus
		{
			ERROR = 0,
			OPEN = 1,
			MESSAGE = 2,
			CLOSE = 3,
		}
		
		public WsObject init(string address, string initMessage)
		{ 
			// this.webSocket = new WebSocket(address);
			this.uri = new Uri(address);
			var factory = new Func<ClientWebSocket>(() => new ClientWebSocket
			{
				Options =
				{
					KeepAliveInterval = TimeSpan.FromSeconds(5),
				}
			});

			this.webSocket = new WebsocketClient(this.uri, factory);
			this.initMessage = initMessage;
			// this.webSocket.WaitTime = new TimeSpan(0, 0, 2);

			this.webSocket.ReconnectTimeout = TimeSpan.FromSeconds(300);
			this.webSocket.ReconnectionHappened.Subscribe(info =>
			{
				if (info.Type != ReconnectionType.Initial)
					this.onOpen();
			});
			this.webSocket.DisconnectionHappened.Subscribe(info =>
			{
				Console.WriteLine("Disconnect happened");
				this.onClose();
			});

			this.webSocket.MessageReceived
			.Where((ResponseMessage message) => message.MessageType == WebSocketMessageType.Text && message.Text != "pong")
			.Subscribe(msg =>
			{
				this.onMessage(msg);
			});
			
			this.connect();
			return this;
		}

		public bool isConnected()
		{
			if (this.webSocket == null || this.webSocket.NativeClient == null) return false;
			return this.webSocket.NativeClient.State == WebSocketState.Open;
		}

		protected virtual void onChanged()
		{
			changed?.Invoke(this, EventArgs.Empty);
		}

		// Handler to be called on a websocket status change, but not messages
		protected virtual void onStatusChanged()
		{
			statusChanged?.Invoke(this, EventArgs.Empty);
		}

		private async Task connect()
		{
			try
			{
				var connectTask = this.webSocket.StartOrFail();
				if (await Task.WhenAny(connectTask, Task.Delay(10000)) != connectTask)
					throw new TimeoutException("Connection timed out");
				await connectTask;
				this.onOpen();
			}
			catch
			{
				this.onError();
			}
		}

		public async Task<bool> disconnect()
		{
			if (this.webSocket == null) return false;
			try
			{
				var stopTask = this.webSocket.StopOrFail(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "disconnect");
				await Task.WhenAny(stopTask, Task.Delay(3000));
			}
			catch { }
			return true;
		}

		// private void onOpen(object sender, EventArgs e)
		private void onOpen()
		{
			send(initMessage);
			status = ConnectionStatus.OPEN;
			onChanged();
			onStatusChanged();
		}

		private void onError()
		{
			status = ConnectionStatus.CLOSE;
			onChanged();
			onStatusChanged();
		}

		private void onMessage(Websocket.Client.ResponseMessage msg)
		{
			status = ConnectionStatus.MESSAGE;

			message = msg.Text;

			onChanged();
		}

		private void onClose()
		{
			status = ConnectionStatus.CLOSE;
			onChanged();
			onStatusChanged();
		}

		public bool send(string msg)
		{
			if(webSocket != null && webSocket.NativeClient.State == WebSocketState.Open)
			{
				webSocket.Send(msg);
				return true;
			} else
			{
				return false;
			}
		}
		public bool isSameAdress(string new_address)
		{
			if (this.uri.OriginalString.Equals(new_address))
			{
				return true;
			}
			return false;
		}

		public static bool isAdressValid(string address)
		{
			if(address.StartsWith("ws://") == true) return true;
			if(address.StartsWith("wss://") == true) return true;
			return false;
		}
	}
}
