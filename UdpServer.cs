using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class UdpServer
{

    enum MessageType
    {
        CONFIRM = 0x00,
        REPLY = 0x01,
        AUTH = 0x02,
        JOIN = 0x03,
        MSG = 0x04,
        ERR = 0xFE,
        BYE = 0xFF
    }
    public string server;

    public int port;
    public List<User> users;
    public int confirmationTimeout;
    public int maxRetransmissions;
    public int currentMessageId = 0;

    public int GetMessageId()
    {
        currentMessageId++;
        return currentMessageId;
    }

    public UdpServer(string server, int port, List<User> users, int confirmationTimeout, int maxRetransmissions)
    {
        this.server = server;
        this.port = port;
        this.users = users;

    }
    public async Task StartListening()
    {
        UdpClient client = new UdpClient(port);
        try
        {
            while (true)
            {
                UdpReceiveResult result;
                try
                {
                    result = await client.ReceiveAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // This will be thrown when the UdpClient is closed while awaiting ReceiveAsync
                    break;
                }
                string clientEndPoint = result.RemoteEndPoint.ToString();
                if (!users.Contains(new User(clientEndPoint, "udp")))
                {
                    users.Add(new User(clientEndPoint, "udp"));
                    //Console.WriteLine($"Saved new IP: {clientEndPoint}");
                }
                User? user = users.Find(x => x.clientEndPoint == clientEndPoint);
                if (user == null)
                {
                    Console.WriteLine("ERR: No user found");
                    return;
                }
                HandleClientAsync(client, result, user);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERR " + ex.Message);
        }
        finally
        {
            Console.WriteLine("Server is shutting down.");
        }
    }
    public async Task HandleClientAsync(UdpClient client, UdpReceiveResult messageBytes, User user)
    {
        string clientEndPoint = messageBytes.RemoteEndPoint.ToString();
        byte[] serverResponse = messageBytes.Buffer;
        if (clientEndPoint == null)
        {
            Console.WriteLine("ERR: No client endpoint");
            return;
        }
        MessageType msgType = (MessageType)serverResponse[0];
        int receivedMessageId = BitConverter.ToUInt16(new byte[] { serverResponse[1], serverResponse[2] }, 0);
        if (msgType == MessageType.AUTH)
        {

            string receivedUsername = ExtractString(serverResponse, startIndex: 3);
            string receivedDisplayName = ExtractString(serverResponse, startIndex: 3 + receivedUsername.Length + 1);
            string receivedSecret = ExtractString(serverResponse, startIndex: 3 + receivedUsername.Length + 1 + receivedDisplayName.Length + 1);
            user.HandleAuth(receivedUsername, receivedDisplayName, receivedSecret);
            user.HandleJoin("default");
            Console.WriteLine("RECV " + user.clientEndPoint + " | AUTH");
            SendConfirm(receivedMessageId, client, user);

            int replyMsgId = SendReply(receivedMessageId, client, user, "Auth success");
            if (WaitConfirm(replyMsgId, client, user))
            {
                Console.WriteLine("RECV " + user.clientEndPoint + " | CONFIRM");
            }
            else
            {
                Console.WriteLine("ERR: No confirmation received");
            }
            //TODO send msg to all users in default chanel
            //for performance reasons
            Thread.Sleep(100);
            //for tcp users
            users.ForEach(x =>
                {
                    if (x.ChanelId != null)
                    {
                        x.SendMsgTcp("MSG FROM Server IS " + receivedDisplayName + " has joined default" + "\r\n", "default");
                    }
                });
            //for udp users
            var messageBuilder = new UdpMessageBuilder();
            messageBuilder.AddMessageType((byte)MessageType.MSG);
            int messageId = GetMessageId();
            messageBuilder.AddMessageId(messageId);
            messageBuilder.AddStringWithDelimiter("Sever");
            messageBuilder.AddStringWithDelimiter(receivedDisplayName + " has joined " + "default");
            byte[] messageToSend = messageBuilder.GetMessage();
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.ChanelId == user.ChanelId)
                    {
                        SendMsgUdp(messageToSend, messageId, client, user);
                    }
                });

        }
        else if (msgType == MessageType.JOIN)
        {
            string receivedChanelId = ExtractString(serverResponse, startIndex: 3);
            string receivedDisplayName = ExtractString(serverResponse, startIndex: 3 + receivedChanelId.Length + 1);
            user.HandleJoin(receivedChanelId);
            Console.WriteLine("RECV " + user.clientEndPoint + " | JOIN");
            SendConfirm(receivedMessageId, client, user);
            int replyMsgId =SendReply(receivedMessageId, client, user, "Join success");
            if (WaitConfirm(replyMsgId, client, user))
            {
                Console.WriteLine("RECV " + user.clientEndPoint + " | CONFIRM");
            }
            else
            {
                Console.WriteLine("ERR: No confirmation received");
            }
            //for performance reasons
            Thread.Sleep(100);
            //for tcp users
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.ChanelId == user.ChanelId)
                    {
                        x.SendMsgTcp("MSG FROM Server IS " + receivedDisplayName + " has joined " + receivedChanelId + "\r\n", receivedChanelId);
                    }
                });
            //for udp users
            var messageBuilder = new UdpMessageBuilder();
            messageBuilder.AddMessageType((byte)MessageType.MSG);
            int messageId = GetMessageId();
            messageBuilder.AddMessageId(messageId);
            messageBuilder.AddStringWithDelimiter("Server");
            messageBuilder.AddStringWithDelimiter(receivedDisplayName + " has joined " + receivedChanelId);
            byte[] messageToSend = messageBuilder.GetMessage();
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.ChanelId == user.ChanelId)
                    {
                        SendMsgUdp(messageToSend, messageId, client, user);
                    }
                });

        }
        else if (msgType == MessageType.MSG)
        {
            string receivedDisplayName = ExtractString(serverResponse, startIndex: 3);
            string messageContents = ExtractString(serverResponse, startIndex: 3 + receivedDisplayName.Length + 1);
            Console.WriteLine("RECV " + user.clientEndPoint + " | MSG");
            SendConfirm(receivedMessageId, client, user);

            //Send to all users in the chanel
            //for performance reasons
            Thread.Sleep(100);
            //for tcp users
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.Username != user.Username && x.ChanelId == user.ChanelId)
                    {
                        x.SendMsgTcp("MSG FROM " + receivedDisplayName + " IS " + messageContents + "\r\n", user.ChanelId);
                    }
                });
            //for udp users
            var messageBuilder = new UdpMessageBuilder();
            messageBuilder.AddMessageType((byte)MessageType.MSG);
            int messageId = GetMessageId();
            messageBuilder.AddMessageId(messageId);
            messageBuilder.AddStringWithDelimiter(receivedDisplayName);
            messageBuilder.AddStringWithDelimiter(receivedDisplayName + ": " + messageContents);
            byte[] messageToSend = messageBuilder.GetMessage();
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.ChanelId == user.ChanelId && x.Username != user.Username)
                    {
                        SendMsgUdp(messageToSend, messageId, client, user);
                    }
                });

        }
        else if (msgType == MessageType.BYE)
        {
            //Console.WriteLine("Server has closed the connection.");
            SendConfirm(receivedMessageId, client, user);
            //Send to all users that user has left
            //for performance reasons
            Thread.Sleep(100);
            //for tcp users
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.Username != user.Username && x.ChanelId == user.ChanelId)
                    {
                        x.SendMsgTcp("MSG FROM Server IS " + user.DisplayName + " has left the " + user.ChanelId + "\r\n", user.ChanelId);
                    }
                });
            //for udp users
            var messageBuilder = new UdpMessageBuilder();
            messageBuilder.AddMessageType((byte)MessageType.BYE);
            int messageId = GetMessageId();
            messageBuilder.AddMessageId(messageId);
            byte[] messageToSend = messageBuilder.GetMessage();
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.ChanelId == user.ChanelId)
                    {
                        SendMsgUdp(messageToSend, messageId, client, user);
                    }
                });

            users.Remove(user);
        }
    }

    public bool WaitConfirm(int messageId, UdpClient client, User user)
    {
        try
        {
            string clientIp = user.clientEndPoint.Split(':')[0];
            int clientPort = int.Parse(user.clientEndPoint.Split(':')[1]);
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(clientIp), clientPort);
            byte[] serverResponse = client.Receive(ref endpoint);
            // Check if the response is a "CONFIRM" message
            if (serverResponse[0] == (byte)MessageType.CONFIRM)
            {
                // Extract the message ID from the response
                byte[] responseMessageIdBytes = new byte[] { serverResponse[1], serverResponse[2] };
                if (BitConverter.ToInt16(responseMessageIdBytes, 0) != messageId)
                {
                    return false;
                }
                else
                {
                    int responseMessageId = BitConverter.ToInt16(responseMessageIdBytes, 0);
                    return true;
                }
            }
            return false;

        }
        catch (OperationCanceledException)
        {
            //Console.WriteLine("Confirmation timed out");
        }
        catch (SocketException ex)
        {
            if (ex.SocketErrorCode == SocketError.TimedOut)
            {
                //Console.WriteLine("Confirmation timed out");

            }
            else
            {
                Console.WriteLine("ERR: " + ex.Message);
            }
        }
        return false;
    }

    public void SendConfirm(int messageId, UdpClient client, User user)
    {
        byte[] message = new byte[1 + 2];
        message[0] = (byte)MessageType.CONFIRM; // CONFIRM message type
        byte[] expectedMessageIdBytes = BitConverter.GetBytes((ushort)messageId);
        Array.Copy(expectedMessageIdBytes, 0, message, 1, expectedMessageIdBytes.Length);
        string clientIp = user.clientEndPoint.Split(':')[0];
        int clientPort = int.Parse(user.clientEndPoint.Split(':')[1]);
        client.Send(message, message.Length, clientIp, clientPort);
        Console.WriteLine("SENT " + user.clientEndPoint + " | CONFIRM");
    }

    public int SendReply(int replyMessageId, UdpClient client, User user, string messageContent)
    {
        var messageBuilder = new UdpMessageBuilder();
        messageBuilder.AddMessageType((byte)MessageType.REPLY);
        int messageId = GetMessageId();
        messageBuilder.AddMessageId(messageId);
        messageBuilder.AddResult(byte.Parse("1"));
        messageBuilder.AddRefMessageId(replyMessageId);
        messageBuilder.AddStringWithDelimiter(messageContent);
        byte[] message = messageBuilder.GetMessage();
        string clientIp = user.clientEndPoint.Split(':')[0];
        int clientPort = int.Parse(user.clientEndPoint.Split(':')[1]);
        client.Send(message, message.Length, clientIp, clientPort);
        Console.WriteLine("SENT " + user.clientEndPoint + " | REPLY");
        return messageId;
    }

    public void SendMsgUdp(byte[] message, int messageId, UdpClient client, User user)
    {
        string clientIp = user.clientEndPoint.Split(':')[0];
        int clientPort = int.Parse(user.clientEndPoint.Split(':')[1]);
        client.Send(message, message.Length, clientIp, clientPort);
        int attempts = 0;
        if (!WaitConfirm(messageId, client, user))
        {
            while (attempts < maxRetransmissions)
            {
                messageId = GetMessageId();
                UdpMessageBuilder.ReplaceMessageId(message, messageId);
                client.Send(message, message.Length, server, port);
                if (WaitConfirm(messageId, client, user))
                {
                    Console.WriteLine("SENT " + user.clientEndPoint + " | MSG");

                    break;
                }
                else
                {
                    if (attempts == maxRetransmissions)
                    {
                        break;
                    }
                    attempts++;
                }
            }
        }
        else
        {
            Console.WriteLine("SENT " + user.clientEndPoint + " | MSG");
        }

    }

    private static string ExtractString(byte[] bytes, int startIndex)
    {
        // Find the null terminator
        int nullIndex = Array.IndexOf(bytes, (byte)0, startIndex);
        if (nullIndex == -1)
        {
            // Handle the case where there's no null terminator
            throw new Exception("Null terminator not found");
        }

        // Extract the string
        int stringLength = nullIndex - startIndex;
        return System.Text.Encoding.UTF8.GetString(bytes, startIndex, stringLength);
    }

}