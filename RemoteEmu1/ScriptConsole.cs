using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Threading;

namespace RemoteEmu1
{
    /// <summary>
    /// I/O for each connected client
    /// </summary>
    class ScriptConsoleClient
    {
        TcpClient Client;                   // the client and its I/O stream
        ScriptConsole Server;               // TCP console server
        StringBuilder InStr;                // the incoming command line
        byte[] Buffer;                      // bytes received on each read
        bool IsSending;                     // true if an async send is in progress
        bool IsClosed;                      // true if Close method has been called

        public IPAddress IpAddr
        {
            get
            {
                return ((IPEndPoint)Client.Client.RemoteEndPoint).Address;
            }
        }

        public int Port
        {
            get
            {
                return ((IPEndPoint)Client.Client.RemoteEndPoint).Port;
            }
        }

        private ScriptConsoleClient() { }            // must provide a client

        public ScriptConsoleClient(TcpClient newTcpClient, ScriptConsole server)
        {
            Client = newTcpClient;
            Server = server;
            InStr = new StringBuilder();
            Buffer = new byte[Client.ReceiveBufferSize];
            IsSending = false;
            IsClosed = false;
        }

        public void Close()
        {
            IsClosed = true;            // start discarding input from async i/o
            for (int i = 0; i < 20 && IsSending; i++)
            {
                // wait for the send to complete
                System.Threading.Thread.Sleep(100);
            }
            Client.GetStream().Close();
            Client.Close();
        }

        public void BeginRead()
        {
            if (IsClosed) return;
            // start listening for input
            Client.GetStream().BeginRead(Buffer, 0, Buffer.Length, OnReceive, this);
        }

        void OnReceive(IAsyncResult ar)
        {
            if (IsClosed) return;       // discard input if in the process of closing

            // read the incoming bytes
            var c = (ScriptConsoleClient)ar.AsyncState;
            NetworkStream stream = c.Client.GetStream();
            int numRead = stream.EndRead(ar);

            if (numRead > 0)
            {
                // build the input string
                c.InStr.Append(Encoding.ASCII.GetChars(c.Buffer, 0, numRead));
                if (c.InStr.ToString().EndsWith(Environment.NewLine))
                {
                    string cmd = c.InStr.ToString().Trim();
                    Server.SendCmd(this, cmd);              // submit the command for processing
                    c.InStr.Clear();                        // start the next line input
                }

                // listen for the next input
                stream.BeginRead(c.Buffer, 0, c.Buffer.Length, OnReceive, c);
            }
            else
            {
                // remote end has disconnected
                Trace.WriteLine(string.Format("ScriptConsoleClient {0}:{1} is closing", ((IPEndPoint)c.Client.Client.RemoteEndPoint).Address, ((IPEndPoint)c.Client.Client.RemoteEndPoint).Port));
                Close();
                Server.SendDetach(this);        // notify console to remove from list of clients
            }
        }

        public void BeginWrite(string outstr)
        {
            byte[] outbuf = Encoding.ASCII.GetBytes(outstr + Environment.NewLine);
            IsSending = true;
            Client.GetStream().BeginWrite(outbuf, 0, outbuf.Length, OnClientSend, this);
        }

        void OnClientSend(IAsyncResult ar)
        {
            var c = (ScriptConsoleClient)ar.AsyncState;
            c.Client.GetStream().EndWrite(ar);
            c.IsSending = false;
        }
    }

    /// <summary>
    /// TCP console that handles multiple simultaneous clients.
    /// The console executes command strings received from clients and sends results to connected clients
    /// </summary>
    class ScriptConsole : IDisposable
    {
        #region Private Types
        struct ScriptConsoleEvent
        {
            public enum EventType { ProcessCmd, DetachClient };
            public EventType Type;
            public string Cmd;
            public ScriptConsoleClient SrcClient;
        }
        #endregion

        #region Private Data
        // TCP server communications
        TcpListener Console;                                // TCP console
        List<ScriptConsoleClient> Client;                   // list of connected clients
        bool IsListening;                                   // indicates when console is not closed
        // command processing queue
        BlockingCollection<ScriptConsoleEvent> Clientq;     // queue of incoming commands and events from attached clients
        BackgroundWorker Bw;                                // thread to service incoming commands and events
        CancellationTokenSource TokenSource;                // creates token for stopping the processing queue
        // execution of commands
        Dictionary<string, IScriptableObject> ScriptObj;    // refs to objects that run commands
        // object support
        bool Disposed = false;                              // allow multiple calls to Dispose
        #endregion

        #region Public Methods
        public ScriptConsole(IPEndPoint tcpEndPt)
        {
            // create the listener
            Console = new TcpListener(tcpEndPt);
            Client = new List<ScriptConsoleClient>();              // start with empty list of connected clients
            IsListening = true;

            ScriptObj = new Dictionary<string, IScriptableObject>();    // start with empty list of scriptable objects

            // set up queue for incoming commands from clients
            Clientq = new BlockingCollection<ScriptConsoleEvent>();
            Bw = new BackgroundWorker();
            Bw.WorkerSupportsCancellation = false;
            Bw.WorkerReportsProgress = true;
            TokenSource = new CancellationTokenSource();
            CancellationToken token = TokenSource.Token;
            Bw.DoWork += Console_DoWork;
            Bw.RunWorkerAsync(token);

            // listen for connections
            Console.Start();
            Console.BeginAcceptTcpClient(OnClientConnect, Console);
            Trace.WriteLine(string.Format("ScriptConsole listening on {0}:{1}", tcpEndPt.Address, tcpEndPt.Port));
        }

        public void Close()
        {
            // close the listener
            Trace.WriteLine("ScriptConsole Tcp listener closing");
            IsListening = false;
            Console.Stop();             // All clients will close as the connection ends

            // shut down the command processing queue
            TokenSource.Cancel();
        }
        #endregion

        #region Command processing queue
        void Console_DoWork(object sender, DoWorkEventArgs e)
        {
            Debug.WriteLine("Console command processing queue started");
            var token = (CancellationToken)e.Argument;
            try
            {
                foreach (var evt in Clientq.GetConsumingEnumerable(token))
                {
                    Debug.WriteLine("Console command processing queue event received");
                    switch (evt.Type)
                    {
                        case ScriptConsoleEvent.EventType.ProcessCmd:
                            // parse the input string and execute the command
                            string result = Parse(evt.Cmd);
                            // send the input and the result to all clients
                            SendResultToClients(evt.Cmd, result);
                            break;
                        case ScriptConsoleEvent.EventType.DetachClient:
                            Client.Remove(evt.SrcClient);
                            Trace.WriteLine("ScriptConsole removed client");
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Console command processing queue exiting");
            }
        }

        public void SendCmd(ScriptConsoleClient src, string cmd)
        {
            var evt = new ScriptConsoleEvent();
            evt.Cmd = cmd;
            evt.Type = ScriptConsoleEvent.EventType.ProcessCmd;
            evt.SrcClient = src;
            Clientq.Add(evt);
        }

        public void SendDetach(ScriptConsoleClient src)
        {
            var evt = new ScriptConsoleEvent();
            evt.Cmd = null;
            evt.Type = ScriptConsoleEvent.EventType.DetachClient;
            evt.SrcClient = src;
            Clientq.Add(evt);
        }
        #endregion

        #region Communications Methods
        void OnClientConnect(IAsyncResult ar)
        {
            if (!IsListening) return;       // refuse new connections if in the process of closing

            // accept the connection and add it to the list of clients
            var listener = (TcpListener)ar.AsyncState;
            TcpClient newTcpClient = listener.EndAcceptTcpClient(ar);
            var newClient = new ScriptConsoleClient(newTcpClient, this);
            Client.Add(newClient);
            Trace.WriteLine(string.Format("ScriptConsole added client at {0}:{1}", newClient.IpAddr, newClient.Port));

            // start listening for input from the new client
            newClient.BeginRead();
            newClient.BeginWrite("Attached to console");

            // listen for the next connection
            Console.BeginAcceptTcpClient(OnClientConnect, Console);
        }

        void SendResultToClients(string input, string result)
        {
            if (!IsListening) return;           // don't start a new send if console is closing
            foreach (ScriptConsoleClient c in Client)
            {
                c.BeginWrite(input);
                c.BeginWrite(result);
            }
        }
        #endregion


        public void RegisterScriptableObject(IScriptableObject sobj)
        {
            ScriptObj.Add(sobj.ScriptableName, sobj);
        }

        string Parse(string inputln)
        {
            // TODO parse input command and run the command
            string [] tok = inputln.Split(new char[] { ' ' });
            return "Result: " + inputln;
        }

        #region Dispose Pattern
        public void Dispose()
        {
            Dispose(true);          // indicate the object is being disposed and object refernces need to be disposed
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;       // allow multiple calls
            if (disposing)
            {
                // this object is being disposed
                // free other objects that implement IDisposable
                Close();                        // finishes async operations and closes interfaces
            }
            Disposed = true;
        }
        #endregion

    }

    // TODO 
    // static methods to parse the string value returned when a command is executed
    //     e.g. GetReturnAsDouble(string), GetReturnAsInt(string), GetReturnAsBool(string), etc.
}
