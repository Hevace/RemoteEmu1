// Base classes and support methods for parsing and creating ICD messages
using System;
using System.Collections.Generic;

namespace RemoteEmu1
{
    /// <summary>
    /// Base class for individual data fields of a message
    /// </summary>
    public abstract class MessageItem
    {
        /// <summary>
        /// Value of the message item in natural units
        /// </summary>
        public virtual double value { get; set; }

        /// <summary>
        /// Multiplier to convert double value to integer representation, e.g., 65536/1000
        /// </summary>
        protected double scale { get; }

        /// <summary>
        /// Offset in natural units added to value before conversion to integer representation
        /// </summary>
        protected double offset { get; }

        protected MessageItem(double scale, double offset)
        {
            this.value = 0.0;
            this.scale = scale;
            this.offset = offset;
        }

        /// <summary>
        /// Convert the message item to a byte stream
        /// </summary>
        /// <returns></returns>
        public abstract byte[] ToBytes();

        /// <summary>
        /// Convert a byte stream to the value of the message item.
        /// </summary>
        /// <param name="msg">Byte stream in network order</param>
        /// <param name="offset">Starting byte of message item</param>
        /// <returns></returns>
        public abstract double Parse(byte[] msg, int offset);
    }

    public class MessageItemDouble : MessageItem
    {
        public MessageItemDouble(double initial) : base(1.0, 0.0)       // doubles always have scale=1 and offset=0
        {
            value = initial;
        }

        /// <summary>
        /// Convert a double to a byte stream
        /// </summary>
        /// <returns></returns>
        public override byte[] ToBytes()
        {
            return ConvertBytes.HostToNetwork(value);
        }

        public override double Parse(byte[] msg, int offset)
        {
            value = ConvertBytes.NetworkToHostDouble(msg, offset);
            return value;
        }
    }

    public class MessageItemUint16 : MessageItem
    {
        public override double value
        {
            set
            {
                // silently clip the value to the limits of the integer field
                double x = (value + offset) / scale;
                if (x > UInt16.MaxValue) x = UInt16.MaxValue;
                if (x < UInt16.MinValue) x = UInt16.MinValue;
                this.value = x;
            }
        }
        public MessageItemUint16(double initial, double scale, double offset) : base(scale, offset)
        {
            value = initial;        // assign with limits checks
        }

        public override byte[] ToBytes()
        {
            double x = (value + offset) / scale;
            if (x > UInt16.MaxValue) x = UInt16.MaxValue;
            if (x < UInt16.MinValue) x = UInt16.MinValue;
            return ConvertBytes.HostToNetwork((UInt16)x);
        }

        public override double Parse(byte[] msg, int offset)
        {
            UInt16 x = ConvertBytes.NetworktoHostUint16(msg, offset);
            value = x * scale + offset;
            return value;
        }
    }

    public class MessageItemBitField : MessageItem
    {
        readonly int BaseTypeLength;
        struct BitFieldDef
        {
            public uint offset;            // bit position of the LSB of the field
            public uint numbits;           // number of bits in the field
            public uint value;             // value of the bit field
        }
        Dictionary<string, BitFieldDef> field;      // defines the bit fields in the message
        public MessageItemBitField(uint length) : base(1.0, 0.0)
        {
            if (length == 0) throw new ArgumentOutOfRangeException("length", "Length cannot be zero");
            if (length <= 8) BaseTypeLength = 8;
            else if (length <= 16) BaseTypeLength = 16;
            else if (length <= 32) BaseTypeLength = 32;
            else throw new ArgumentOutOfRangeException("length", "Fields of this length are not supported");

            field = new Dictionary<string, BitFieldDef>();
        }

        private MessageItemBitField(): base(1.0, 0.0) { }       // must supply length

        public void AddField(string name, uint offset, uint numbits)
        {
            // Does not check for overlapping fields
            if (numbits == 0) throw new ArgumentOutOfRangeException("numbits", "Bit field length of zero is not allowed");
            if (offset + numbits > BaseTypeLength) throw new ArgumentOutOfRangeException("offset/numbits", "Bit field does not fit into base type");

            BitFieldDef bf;
            bf.offset = offset;
            bf.numbits = numbits;
            bf.value = 0;
            field.Add(name, bf);
        }

        /// <summary>
        /// Get or set the value of a single bit field
        /// </summary>
        /// <param name="name">Name of the bit field</param>
        /// <returns>Value of the named bit field</returns>
        public uint this[string name]
        {
            get
            {
                return field[name].value;           // return the value of a single bit field
            }
            set
            {
                BitFieldDef bf = field[name];
                if (value > Math.Pow(2,bf.numbits) - 1) throw new ArgumentOutOfRangeException("value", "Out of range for size of bit field");

                // set the value of the single bit field
                bf.value = value;
                field[name] = bf;

                // update the value of the base type
                uint mask = (uint)(Math.Pow(2, bf.numbits) - 1) << (int)bf.offset;
                uint baseval = (uint)this.value;
                baseval &= ~mask;
                baseval |= value << (int)bf.offset;
                this.value = baseval;
            }
        }

        /// <summary>
        /// Get or set the value of all bit fields at once
        /// </summary>
        public override double value
        {
            set
            {
                if (value > Math.Pow(2, BaseTypeLength) - 1) throw new ArgumentOutOfRangeException("value", "Too large for base type");
                if (value < 0) throw new ArgumentOutOfRangeException("value", "Cannot be negative");

                // update the value of the base type
                this.value = value;

                // update the value of each bit field
                foreach (var p in field)
                {
                    BitFieldDef bf = p.Value;
                    bf.value = ((uint)value >> (int)bf.offset) & (uint)(Math.Pow(2, bf.numbits - 1));
                    field[p.Key] = bf;
                }
            }
        }

        public override byte[] ToBytes()
        {
            byte[] b = ConvertBytes.HostToNetwork((uint)value);
            byte[] bout = new byte[BaseTypeLength];
            Array.Copy(b, 4-BaseTypeLength, bout, 0, bout.Length);
            return bout;
        }

        public override double Parse(byte[] msg, int offset)
        {
            byte[] b = new byte[sizeof(uint)];
            Array.Copy(msg, offset, b, 4-BaseTypeLength, BaseTypeLength);
            uint bits = ConvertBytes.NetworktoHostUint32(b, 0);
            value = bits;
            return value;
        }
    }

    /// <summary>
    /// Base class for the messages defined in the ICD
    /// </summary>
    public abstract class MessageBase
    {
        public readonly byte header;
        public readonly byte id;
        public byte seq;
        public readonly byte len;
        public Dictionary<string, MessageItem> data;    // defines the contents of each field
        public List<MessageItem> fields;                // defines the order of the fields within the message
        public MessageBase(byte id, byte len)
        {
            this.header = 0xaa;
            this.id = id;
            this.len = len;
            this.seq = 0;
        }
        public virtual void AddField(string fieldname, MessageItem m)
        {
            data[fieldname] = m;
            fields.Add(m);
        }

        public virtual double this[string itemname]
        {
            get { return data[itemname].value; }
            set { data[itemname].value = value; }
        }

        public virtual byte[] ToBytes()
        {
            byte[] msg = new byte[len + 4];
            // message header
            msg[0] = header;
            msg[1] = id;
            msg[2] = seq++;
            msg[3] = len;
            // convert message items to bytes, in order, with CRC
            int idx = 4;
            foreach (MessageItem item in fields)
            {
                byte[] itemb = item.ToBytes();
                itemb.CopyTo(msg, idx);
                idx += itemb.Length;
            }
            // TODO append the CRC
            return msg;
        }

    }

    #region Byte Order Conversions
    public class ConvertBytes
    {
        public static byte[] HostToNetwork(double x)
        {
            byte[] b = BitConverter.GetBytes(x);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return b;
        }

        public static byte[] HostToNetwork(UInt16 x)
        {
            byte[] b = BitConverter.GetBytes(x);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return b;
        }

        public static double NetworkToHostDouble(byte[] msg, int offset)
        {
            byte[] b = new byte[sizeof(double)];
            Array.Copy(msg, offset, b, 0, b.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToDouble(b, 0);
        }

        public static UInt32 NetworktoHostUint32(byte[] msg, int offset)
        {
            byte[] b = new byte[sizeof(UInt32)];
            Array.Copy(msg, offset, b, 0, b.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToUInt32(b, 0);
        }

        public static UInt16 NetworktoHostUint16(byte[] msg, int offset)
        {
            byte[] b = new byte[sizeof(UInt16)];
            Array.Copy(msg, offset, b, 0, b.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToUInt16(b, 0);
        }
    }
    #endregion
}
