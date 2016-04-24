using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace RemoteEmu1
{
    class ScriptConsole : IDisposable
    {
        #region Private Types
        /// <summary>
        /// State data for each connected client
        /// </summary>
        struct ClientData
        {
            public TcpClient Client;                    // the client and its I/O stream
            public StringBuilder InStr;                 // the incoming command line
            public byte[] Buffer;                       // bytes received on each read
            public bool IsSending;                      // true if an async send is in progress
        }
        #endregion

        #region Private Data
        // TCP server communications
        TcpListener Console;                                // TCP console
        List<ClientData> Client;                            // list of connected clients
        bool IsListening;                                   // indicates when console is not closed
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
            Client = new List<ClientData>();              // start with empty list of connected clients
            IsListening = true;

            ScriptObj = new Dictionary<string, IScriptableObject>();    // start with empty list of scriptable objects

            // listen for connections
            Console.Start();
            Console.BeginAcceptTcpClient(OnClientConnect, new object());

            Trace.WriteLine(string.Format("ScriptConsole listening on {0}:{1}", tcpEndPt.Address, tcpEndPt.Port));
        }

        public void Close()
        {
            // close the listener
            Trace.WriteLine("ScriptConsole closing");
            IsListening = false;
            Console.Stop();

            foreach (var c in Client)
            {
                Trace.WriteLine(string.Format("ScriptConsole closing client at {0}:{1}", ((IPEndPoint)c.Client.Client.RemoteEndPoint).Address, ((IPEndPoint)c.Client.Client.RemoteEndPoint).Port));
                for (int i = 0; i < 20 && c.IsSending; i++)
                {
                    // wait for the send to complete
                    System.Threading.Thread.Sleep(100);
                }
                c.Client.GetStream().Close();
                c.Client.Close();
            }
        }
        #endregion

        #region Communications Methods
        void OnClientConnect(IAsyncResult ar)
        {
            if (!IsListening) return;       // refuse new connections if in the process of closing

            // accept the connection and add it to the list of clients
            ClientData newClient;
            newClient.Client = Console.EndAcceptTcpClient(ar);
            newClient.InStr = new StringBuilder();
            newClient.Buffer = new byte[newClient.Client.ReceiveBufferSize];
            newClient.IsSending = false;
            Client.Add(newClient);
            Trace.WriteLine(string.Format("ScriptConsole added client at {0}:{1}", ((IPEndPoint)newClient.Client.Client.RemoteEndPoint).Address, ((IPEndPoint)newClient.Client.Client.RemoteEndPoint).Port));

            // start listening for input
            NetworkStream stream = newClient.Client.GetStream();
            stream.BeginRead(newClient.Buffer, 0, newClient.Buffer.Length, OnClientReceive, newClient);

            // listen for the next connection
            Console.BeginAcceptTcpClient(OnClientConnect, new object());
        }

        void OnClientReceive(IAsyncResult ar)
        {
            if (!IsListening) return;       // discard input if in the process of closing

            // read the incoming bytes
            var cd = (ClientData)ar.AsyncState;
            NetworkStream stream = cd.Client.GetStream();
            int numRead = stream.EndRead(ar);

            if (numRead > 0)
            {
                // build the input string
                cd.InStr.Append(Encoding.ASCII.GetChars(cd.Buffer, 0, numRead));
                if (cd.InStr.ToString().EndsWith(Environment.NewLine))
                {
                    string cmd = cd.InStr.ToString().Trim();
                    // parse the input string and execute the command   // TODO run command in another thread
                    string result = Parse(cmd);
                    // send the input and the result to all clients
                    SendResultToClients(cmd, result);
                    // start the next line input
                    cd.InStr.Clear();
                }

                // listen for the next input
                stream.BeginRead(cd.Buffer, 0, cd.Buffer.Length, OnClientReceive, cd);
            }
            else
            {
                // remote end has disconnected
                Trace.WriteLine(string.Format("ScriptConsole closing client at {0}:{1}", ((IPEndPoint)cd.Client.Client.RemoteEndPoint).Address, ((IPEndPoint)cd.Client.Client.RemoteEndPoint).Port));
                for (int i = 0; i < 20 && cd.IsSending; i++)
                {
                    // wait for the send to complete
                    System.Threading.Thread.Sleep(100);
                }
                cd.Client.GetStream().Close();
                cd.Client.Close();
                Client.Remove(cd);
            }
        }

        void OnClientSend(IAsyncResult ar)
        {
            var cd = (ClientData)ar.AsyncState;
            NetworkStream stream = cd.Client.GetStream();
            stream.EndWrite(ar);
            cd.IsSending = false;
        }

        void SendResultToClients(string input, string result)
        {
            if (!IsListening) return;           // don't start a new send if console is closing

            byte[] inputBytes = Encoding.ASCII.GetBytes(input+Environment.NewLine);
            byte[] resultBytes = Encoding.ASCII.GetBytes(result+Environment.NewLine);

            for (int i = 0; i < Client.Count; i++)
            {
                ClientData cd = Client[i];
                cd.IsSending = true;
                NetworkStream stream = cd.Client.GetStream();
                stream.BeginWrite(inputBytes, 0, inputBytes.Length, OnClientSend, cd);
                stream.BeginWrite(resultBytes, 0, resultBytes.Length, OnClientSend, cd);
            }
        }
        #endregion


        public void RegisterScriptableObject(IScriptableObject sobj)
        {
            ScriptObj.Add(sobj.ScriptableName, sobj);
        }

        string Parse(string inputln)
        {
            // TODO parse input command and run the command. Don't execute the command in the async NetworkStream receive thread
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
