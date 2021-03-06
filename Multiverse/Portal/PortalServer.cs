#region Header
//   Vorspire    _,-'/-'/  PortalServer.cs
//   .      __,-; ,'( '/
//    \.    `-.__`-._`:_,-._       _ , . ``
//     `:-._,------' ` _,`--` -: `_ , ` ,' :
//        `---..__,,--'  (C) 2016  ` -'. -'
//        #  Vita-Nex [http://core.vita-nex.com]  #
//  {o)xxx|===============-   #   -===============|xxx(o}
//        #        The MIT License (MIT)          #
#endregion

#region References
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
#endregion

namespace Multiverse
{
	public sealed class PortalServer : PortalTransport
	{
		public static Func<Socket, PortalClient> CreateClientHandler;

		private volatile List<PortalClient> _Clients = new List<PortalClient>();

		private readonly object _ClientsLock = new object();

		public IEnumerable<PortalClient> Clients
		{
			get
			{
				if (_Clients == null)
				{
					yield break;
				}

				PortalClient c;

				var i = _Clients.Count;

				while (--i >= 0)
				{
					lock (_ClientsLock)
					{
						if (i > _Clients.Count)
						{
							continue;
						}

						c = _Clients[i];

						if (c == null)
						{
							_Clients.RemoveAt(i);

							ToConsole("C?/?.?.?.? Disconnected [{0} Active]", _Clients.Count);
						}
						else if (!c.IsAlive)
						{
							_Clients.RemoveAt(i);

							ToConsole("{0} Disconnected [{1} Active]", c, _Clients.Count);
						}
						else
						{
							yield return c;
						}
					}
				}
			}
		}

		private volatile Socket _Server;

		public override Socket Socket { get { return _Server; } }

		public PortalServer()
		{
			_Server = new Socket(Portal.Server.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
		}

		public bool IsConnected(ushort serverID)
		{
			if (Portal.IsClient)
			{
				return Portal.ClientID == serverID && IsAlive;
			}

			return Clients.Any(c => c != null && c.ServerID == serverID);
		}

		protected override void OnStart()
		{
			try
			{
				ToConsole("Listener: Binding: {0}", Portal.Server);

				_Server.LingerState.Enabled = false;
#if !MONO
				_Server.ExclusiveAddressUse = false;
#endif
				_Server.Bind(Portal.Server);
				_Server.Listen(8);

				ToConsole("Listener: Bound: {0}", Portal.Server);
			}
			catch (Exception e)
			{
				ToConsole("Listener: Failed", e);

				Dispose();
				return;
			}

			try
			{
				do
				{
					if (_Server != null && _Server.Poll(-1, SelectMode.SelectRead))
					{
						var s = _Server.Accept();

						PortalClient client = null;

						if (CreateClientHandler != null)
						{
							client = CreateClientHandler(s);
						}

						if (client == null)
						{
							client = new PortalClient(s);
						}

						lock (_ClientsLock)
						{
							_Clients.Add(client);

							ToConsole("{0} Connected [{1} Active]", client, _Clients.Count);
						}

						try
						{
							var thread = new Thread(client.Start)
							{
								Name = client.ToString()
							};
							
							thread.Start();

							while (!thread.IsAlive)
							{
								Thread.Sleep(10);
							}
						}
						catch
						{
							lock (_ClientsLock)
							{
								_Clients.Remove(client);

								ToConsole("{0} Disconnected [{1} Active]", client, _Clients.Count);
							}

							try
							{
								client.Dispose();
							}
							catch
							{ }
						}
					}

					Thread.Sleep(10);
				}
				while (CheckAlive());
			}
			catch (Exception e)
			{
				ToConsole("Listener: Failed", e);
			}

			Dispose();
		}

		public override bool Send(PortalPacket p)
		{
			if (!CheckAlive())
			{
				return false;
			}

			var any = false;

			try
			{
				foreach (var c in Clients)
				{
					try
					{
						if (c.Send(p))
						{
							any = true;
						}
					}
					catch
					{ }
				}
			}
			catch
			{ }

			return any;
		}

		public override bool SendExcept(PortalPacket p, ushort exceptID)
		{
			if (!CheckAlive())
			{
				return false;
			}

			var any = false;

			try
			{
				foreach (var c in Clients)
				{
					try
					{
						if (c.SendExcept(p, exceptID))
						{
							any = true;
						}
					}
					catch
					{ }
				}
			}
			catch
			{ }

			return any;
		}

		public override bool SendTarget(PortalPacket p, ushort targetID)
		{
			if (!CheckAlive())
			{
				return false;
			}

			var any = false;

			try
			{
				foreach (var c in Clients)
				{
					try
					{
						if (c.SendTarget(p, targetID))
						{
							any = true;
						}
					}
					catch
					{ }
				}
			}
			catch
			{ }

			return any;
		}

		public ushort Intern(ushort sid)
		{
			if (sid == UInt16.MaxValue)
			{
				sid = 0;
			}

			if (Clients.Any(c => c.IsIdentified && c.ServerID == sid))
			{
				return Intern(++sid);
			}

			return sid;
		}

		protected override bool CheckAlive(long ticks)
		{
			if (!base.CheckAlive(ticks))
			{
				return false;
			}

			try
			{
				if (_Server == null || !_Server.IsBound || _Clients == null)
				{
					Dispose();
					return false;
				}

				var i = _Clients.Count;

				PortalClient c;

				while (--i >= 0)
				{
					try
					{
						lock (_ClientsLock)
						{
							if (i > _Clients.Count)
							{
								continue;
							}

							c = _Clients[i];

							if (c == null)
							{
								_Clients.RemoveAt(i);

								ToConsole("C?/?.?.?.? Disconnected [{0} Active]", _Clients.Count);
							}
							else if (!c.IsAlive)
							{
								_Clients.RemoveAt(i);

								ToConsole("{0} Disconnected [{1} Active]", c, _Clients.Count);
							}
						}
					}
					catch
					{ }
				}
			}
			catch
			{
				Dispose();
				return false;
			}

			return true;
		}

		protected override void OnDispose()
		{
			base.OnDispose();

			if (_Clients != null)
			{
				var i = _Clients.Count;

				PortalClient c;

				while (--i >= 0)
				{
					try
					{
						lock (_ClientsLock)
						{
							if (i >= _Clients.Count)
							{
								continue;
							}

							c = _Clients[i];

							_Clients.RemoveAt(i);
						}

						if (c != null)
						{
							c.Dispose();
						}
					}
					catch
					{ }
				}

				lock (_ClientsLock)
				{
					_Clients.Clear();
					_Clients = null;
				}
			}

			_Server = null;
		}

		public override string ToString()
		{
			return String.Format("S{0}/{1}", Portal.ServerID, Portal.Server.Address);
		}
	}
}