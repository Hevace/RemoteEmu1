// Define a class for each message type defined in the ICD

using System.Collections.Generic;

namespace RemoteEmu1
{
    /// <summary>
    /// Dummy for a message type
    /// </summary>
    public class StatusMsg1 : MessageBase, IScriptableObject
    {
        /// <summary>
        /// Define each message field in terms of scale factor, offset, and a default value
        /// </summary>
        /// <param name="id">Single-byte message ID</param>
        /// <param name="len">Single-byte message length including CRC</param>
        public StatusMsg1(byte id, byte len) : base(id, len)
        {
            // Define the message fields here, in the order defined in the ICD
            AddField("f1", new MessageItemDouble(0.0));
            AddField("f2", new MessageItemDouble(1.0));
            AddField("u3", new MessageItemUint16(0.0, 65536.0 / 1.0, 0.0));
            MessageItemBitField bf1 = new MessageItemBitField(16);
            bf1.AddField("fld1", 0, 1);
            bf1.AddField("fld2", 1, 3);
            bf1.AddField("fld3", 4, 6);
            AddField("bf1", bf1);
        }

        /// <summary>
        /// Prevent use of default constructor
        /// </summary>
        private StatusMsg1() : base(0, 0) { }

        public string ScriptableName
        {
            get { return "StatusMsg1"; }
        }

        public CmdReturnData Exec(string cmdname, List<object> param)
        {
            // no commands defined
            return new CmdReturnData();
        }

    }

    public class CmdMsg1 : MessageBase, IScriptableObject
    {
        public CmdMsg1(byte id, byte len) : base(id, len)
        {
            AddField("cfld2", new MessageItemUint16(0, 1.0, 0.0));
            AddField("cfld3", new MessageItemUint16(0, 1.0, 0.0));
        }

        private CmdMsg1() : base(0, 0) { }

        public string ScriptableName
        {
            get { return "CmdMsg1"; }
        }

        public CmdReturnData Exec(string cmdname, List<object> param)
        {
            // no commands defined
            return new CmdReturnData();
        }
    }

    public class CmdMsg2 : MessageBase, IScriptableObject
    {
        public CmdMsg2(byte id, byte len) : base(id, len)
        {
            AddField("cf1", new MessageItemDouble(0));
            AddField("cf2", new MessageItemDouble(0));
        }

        private CmdMsg2() : base(0, 0) { }

        public string ScriptableName
        {
            get { return "CmdMsg2"; }
        }

        public CmdReturnData Exec(string cmdname, List<object> param)
        {
            // no commands defined
            return new CmdReturnData();
        }
    }

}