using System;
using System.IO;
using System.Text;

public class UdpMessageBuilder
{
    private MemoryStream _stream;
    private BinaryWriter _writer;

    public UdpMessageBuilder()
    {
        _stream = new MemoryStream();
        _writer = new BinaryWriter(_stream);
    }

    public static int currentMessageId = 0;

    public static int GetMessageId()
    {
        currentMessageId++;
        return currentMessageId;
    }

    public void AddMessageType(byte messageType)
    {
        _writer.Write(messageType);
    }

    public void AddMessageId(int messageId)
    {
        byte[] messageIdBytes = BitConverter.GetBytes((ushort)messageId);
        // Ensure there's enough space for messageId, assuming a fixed length for simplicity
        // This could be dynamic based on the actual messageId length or a predefined protocol specification
        _writer.Write(new byte[2]); // Placeholder for messageId if it has a fixed length
        Array.Copy(messageIdBytes, 0, _stream.GetBuffer(), 1, messageIdBytes.Length);
    }
    public static void ReplaceMessageId(byte[] message, int messageId)
    {
        // Convert the messageId to 2 bytes.
        byte[] messageIdBytes = BitConverter.GetBytes((ushort)messageId);
        // Replace the 2 bytes right after the messageType byte.
        message[1] = messageIdBytes[0];
        message[2] = messageIdBytes[1];
    }
    public void AddStringWithDelimiter(string value)
    {
        byte[] valueBytes = Encoding.UTF8.GetBytes(value.Trim());
        _writer.Write(valueBytes);
        _writer.Write((byte)0); // Zero byte delimiter
    }

    public void AddResult(byte result)
    {
        _writer.Write(result);
    }

    public void AddRefMessageId(int refMessageId)
    {
        byte[] refMessageIdBytes = BitConverter.GetBytes((ushort)refMessageId);
        _writer.Write(refMessageIdBytes);
    }

    public byte[] GetMessage()
    {
        return _stream.ToArray();
    }
}