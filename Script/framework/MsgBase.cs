using System;
using System.Net.Sockets;
using UnityEngine;

public class MsgBase
{
    //协议名
    public string protoName = "";

    public static byte[] EnCode(MsgBase msgBase)
    {
        string json = JsonUtility.ToJson(msgBase);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    public static MsgBase DeCode(string protoName, byte[] bytes, int offset, int count)
    {
        string s = System.Text.Encoding.UTF8.GetString(bytes, offset, count);
        MsgBase msgBase = (MsgBase) JsonUtility.FromJson(s, Type.GetType(protoName));
        return msgBase;
    }

    public static byte[] EnCodeName(MsgBase msgBase)
    {
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(msgBase.protoName);
        Int16 len = (Int16) nameBytes.Length;
        byte[] bytes = new byte[2 + len];
        bytes[0] = (byte) (len % 256);
        bytes[1] = (byte) (len / 256);
        Array.Copy(nameBytes, 0, bytes, 2, len);
        return bytes;
    }

    public static String DeCodeName(byte[] bytes, int offset, out int count)
    {
        count = 0;
        if (offset + 2 > bytes.Length)
        {
            return "";
        }

        Int16 len = (Int16) ((bytes[offset + 1] << 8) | bytes[offset]);
        if (offset + 2 + len > bytes.Length)
        {
            return "";
        }

        count = 2 + len;
        string name = System.Text.Encoding.UTF8.GetString(bytes, offset + 2, count);
        return name;
    }
}