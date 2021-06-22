using Firesplash.UnityAssets.SocketIO;
using Firesplash.UnityAssets.SocketIO.Internal;
using Firesplash.UnityAssets.SocketIO.MIT;
using Firesplash.UnityAssets.SocketIO.MIT.Packet;
using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Decoder = Firesplash.UnityAssets.SocketIO.MIT.Decoder;
using Encoder = Firesplash.UnityAssets.SocketIO.MIT.Encoder;

internal class SocketIONativeInstance : SocketIOInstance
{
    private ClientWebSocket Socket;

    Thread WebSocketReaderThread, WebSocketWriterThread, PingPongThread;

    string targetAddress;

    Parser parser;

    private BlockingCollection<Tuple<DateTime, string>> sendQueue = new BlockingCollection<Tuple<DateTime, string>>();

    private CancellationTokenSource cTokenSrc;
    private bool waitingForPong;
    private string SocketID;

    internal SocketIONativeInstance(string instanceName, string targetAddress) : base(instanceName, targetAddress)
    {
        SocketIOManager.LogDebug("Creating Native Socket.IO instance for " + instanceName);
        this.InstanceName = instanceName;
        this.targetAddress = "ws" + targetAddress.Substring(4);

        //Initialize MIT-Licensed helpers
        parser = new Parser();

        sendQueue = new BlockingCollection<Tuple<DateTime, string>>();
        cTokenSrc = new CancellationTokenSource();

        Socket = new ClientWebSocket();
    }

    public override void Connect()
    {
        Task.Run(async () =>
        {
            //Kill all remaining threads
            if (WebSocketReaderThread != null && WebSocketReaderThread.IsAlive) WebSocketReaderThread.Abort();
            if (WebSocketWriterThread != null && WebSocketWriterThread.IsAlive) WebSocketWriterThread.Abort();
            if (PingPongThread != null && PingPongThread.IsAlive) PingPongThread.Abort();

            lock (Socket)
            {
                Socket = new ClientWebSocket();
            }
            try
            {
                Uri baseUri = new Uri(targetAddress);
                Uri connectTarget = new Uri(baseUri.Scheme + "://" + baseUri.Host + ":" + baseUri.Port + "/socket.io/?EIO=3&transport=websocket" + (baseUri.Query.Length > 1 ? "&" + baseUri.Query.Substring(1) : ""));
                await Socket.ConnectAsync(connectTarget, cTokenSrc.Token);
                while (Socket.State != WebSocketState.Open) {
                    Thread.Sleep(25);
                };
            }
            catch (Exception e)
            {
                //TODO Timeout?
                SocketIOManager.LogError(InstanceName + ": " + e.Message);
                SIODispatcher.Instance.Enqueue(new Action(() => { RaiseSIOEvent("connect_error", e.Message); }));
                Status = SIOStatus.ERROR;
                return;
            }

            try
            {
                WebSocketReaderThread = new Thread(new ThreadStart(SIOSocketReader));
                WebSocketWriterThread = new Thread(new ThreadStart(SIOSocketWriter));
                PingPongThread = new Thread(new ThreadStart(SIOSocketWatchdog));
            
                WebSocketReaderThread.Start();
                WebSocketWriterThread.Start();
                PingPongThread.Start();
            } 
            catch (Exception e)
            {
                SocketIOManager.LogError("Exception while starting threads on " + InstanceName + ": " + e.ToString());
            }
            //Thread.Sleep(100);

            Status = SIOStatus.CONNECTED;

            SIODispatcher.Instance.Enqueue(new Action(() => { RaiseSIOEvent("connect", null); }));

        });

        base.Connect();
    }

    public override void Close()
    {
        EmitClose();
        Status = SIOStatus.DISCONNECTED;

        //Stop threads ASAP
        cTokenSrc.Cancel();
    }



    internal void RaiseSIOEvent(string EventName)
    {
        RaiseSIOEvent(EventName, null);
    }

    internal override void RaiseSIOEvent(string EventName, string Data)
    {
        base.RaiseSIOEvent(EventName, Data);
    }

    public override void Emit(string EventName)
    {
        EmitMessage(-1, string.Format("[\"{0}\"]", EventName));
        base.Emit(EventName);
    }

    public override void Emit(string EventName, string Data)
    {
        EmitMessage(-1, string.Format("[\"{0}\",{1}]", EventName, Data));
        base.Emit(EventName, Data);
    }

    public override void Emit(string EventName, string Data, bool handleJSONAsPlainText)
    {
        if (!handleJSONAsPlainText)
        {
            try
            {
                UnityEngine.JsonUtility.FromJson(Data, null);
            }
            catch (Exception)
            {
                //We re-use the bool. This happens if the "Data" object contains no valid json data
                handleJSONAsPlainText = true;
            }
        }

        if (handleJSONAsPlainText) EmitMessage(-1, string.Format("[\"{0}\",\"{1}\"]", EventName, Data));
        else EmitMessage(-1, string.Format("[\"{0}\",{1}]", EventName, Data));
        base.Emit(EventName, Data);
    }


    #region Incoming SIO Events
    void EmitMessage(int id, string json)
    {
        EmitPacket(new SocketPacket(EnginePacketType.MESSAGE, SocketPacketType.EVENT, 0, "/", id, json));
    }

    void EmitClose()
    {
        EmitPacket(new SocketPacket(EnginePacketType.MESSAGE, SocketPacketType.DISCONNECT, 0, "/", -1, JsonUtility.ToJson("")));
        EmitPacket(new SocketPacket(EnginePacketType.CLOSE));
    }

    void EmitPacket(SocketPacket packet)
    {
        sendQueue.Add(new Tuple<DateTime, string>(DateTime.UtcNow, Encoder.Encode(packet)));
    }
    #endregion




    private async void SIOSocketReader()
    {
        while (!cTokenSrc.IsCancellationRequested)
        {
            var message = "";
            var binary = new List<byte>();

            READ:
            var buffer = new byte[1024];
            WebSocketReceiveResult res = null;

            try
            {
                res = await Socket.ReceiveAsync(new ArraySegment<byte>(buffer), cTokenSrc.Token);
                if (cTokenSrc.IsCancellationRequested) return;
            }
            catch
            {
                //Something went wrong
                if (cTokenSrc.IsCancellationRequested) return;
                Status = SIOStatus.ERROR;
                SIODispatcher.Instance.Enqueue(new Action(() => { RaiseSIOEvent("disconnect", "server namespace disconnect"); }));
                Socket.Abort();
                break;
            }

            if (res == null)
                goto READ; //we got nothing. Wait for data.

            if (res.MessageType == WebSocketMessageType.Close)
            {
                cTokenSrc.Cancel();
                Status = SIOStatus.DISCONNECTED;
                SIODispatcher.Instance.Enqueue(new Action(() => { RaiseSIOEvent("disconnect", "server namespace disconnect"); }));
                return;
            }
            else if (res.MessageType == WebSocketMessageType.Text)
            {
                if (!res.EndOfMessage)
                {
                    message += Encoding.UTF8.GetString(buffer).TrimEnd('\0');
                    goto READ;
                }
                message += Encoding.UTF8.GetString(buffer).TrimEnd('\0');

                SocketPacket packet = Decoder.Decode(message);

                switch (packet.enginePacketType)
                {
                    case EnginePacketType.OPEN:
                        SocketID = JsonUtility.FromJson<SocketOpenData>(packet.json).sid;

                        SIODispatcher.Instance.Enqueue(new Action(() =>
                        {
                            RaiseSIOEvent("open");
                        }));
                        break;

                    case EnginePacketType.CLOSE:
                        SIODispatcher.Instance.Enqueue(new Action(() =>
                        {
                            RaiseSIOEvent("close");
                        }));
                        break;

                    case EnginePacketType.MESSAGE:
                        if (packet.json == "")
                        {
                            buffer = null;
                            message = "";
                            continue;
                        }

                        if (packet.socketPacketType == SocketPacketType.ACK)
                        {
                            SocketIOManager.LogWarning("ACK is not supported by this library.");
                        }

                        if (packet.socketPacketType == SocketPacketType.EVENT)
                        {
                            SIOEventStructure e = Parser.Parse(packet.json);
                            SIODispatcher.Instance.Enqueue(new Action(() =>
                            {
                                RaiseSIOEvent(e.eventName, e.data);
                            }));
                        }
                        break;

                    case EnginePacketType.PING:
                        EmitPacket(new SocketPacket(EnginePacketType.PONG));
                        break;

                    case EnginePacketType.PONG:
                        waitingForPong = false; //woohoo!
                        break;

                    default:
                        SocketIOManager.LogWarning("Unhandled SIO packet: " + message);
                        break;

                }
            }
            else
            {
                if (!res.EndOfMessage)
                {
                    goto READ;
                }
                SocketIOManager.LogWarning("Received binary message");
            }
            buffer = null;
        }
    }

    private async void SIOSocketWriter()
    {
        while (!cTokenSrc.IsCancellationRequested || sendQueue.Count > 0)
        {
            Thread.Sleep(100);
            var msg = sendQueue.Take(cTokenSrc.Token);
            if (msg.Item1.Add(new TimeSpan(0, 0, 10)) < DateTime.UtcNow)
            {
                continue;
            }
            var buffer = Encoding.UTF8.GetBytes(msg.Item2);
            try
            {
                await Socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, cTokenSrc.Token);
            }
            catch (Exception)
            {
                SIODispatcher.Instance.Enqueue(new Action(() =>
                {
                    RaiseSIOEvent("error");
                }));
                lock (Socket)
                {
                    Socket.Abort();
                    Status = SIOStatus.ERROR;
                }
                break;
            }
        }
    }

    private void SIOSocketWatchdog()
    {
        DateTime pingStart;
        waitingForPong = false;
        System.Random rnd = new System.Random();

        //PingLoop
        while (!cTokenSrc.IsCancellationRequested)
        {
            Thread.Sleep(2500 + rnd.Next(0, 500)); //add some jitter

            if (Socket.State == WebSocketState.Open && Status != SIOStatus.CONNECTED) return; //Something went wrong. Cancel this watchdog

            pingStart = DateTime.Now;
            waitingForPong = true;
            EmitPacket(new SocketPacket(EnginePacketType.PING));

            while (waitingForPong && !cTokenSrc.IsCancellationRequested) //Ping timout of 2000msec
            {
                if (DateTime.Now.Subtract(pingStart).TotalSeconds > 2000 || Socket.State != WebSocketState.Open)
                {
                    //Timeout or socket closed
                    SIODispatcher.Instance?.Enqueue(new Action(() => { RaiseSIOEvent("connect_timeout", null); }));
                    SIODispatcher.Instance?.Enqueue(new Action(() => { RaiseSIOEvent("disconnect", null); }));
                    Thread.Sleep(500); //Wait a moment in favor of the event handler

                    Connect(); //reconnect
                    return; //End this thread (we will start a new one once connected)
                }
                Thread.Sleep(100); // wait for ping timeout
            }
            //If the code lands here, the pong has arrived in time.
        }
    }
}
