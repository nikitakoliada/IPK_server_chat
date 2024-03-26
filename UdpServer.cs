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
    public static int maxRetransmissions;

    public static UdpClient client;

    public static CancellationTokenSource startListinengToken = new CancellationTokenSource();
    public UdpServer(string server, int port, ref List<User> users, int confirmationTimeout, int maxRetransmissions)
    {
        this.server = server;
        this.port = port;
        this.users = users;
        this.confirmationTimeout = confirmationTimeout;
        UdpServer.maxRetransmissions = maxRetransmissions;
    }
    public async Task StartListening(CancellationTokenSource cts)
    {
        client = new UdpClient(port);
        client.Client.ReceiveTimeout = confirmationTimeout;
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    Console.WriteLine("Waiting for a connection...");
                    result = await client.ReceiveAsync(startListinengToken.Token).ConfigureAwait(false);
                    var watch = System.Diagnostics.Stopwatch.StartNew();

                    string clientEndPoint = result.RemoteEndPoint.ToString();
                    bool userAlreadyExists = false;
                    foreach (User compareUser in users)
                    {
                        Console.WriteLine("User: " + compareUser.clientEndPoint + " with the displayname " + (compareUser.DisplayName != null ? compareUser.DisplayName : "null") + " | " + compareUser.Protocol);
                        if (compareUser.clientEndPoint == clientEndPoint)
                        {
                            userAlreadyExists = true;
                        }
                    }
                    if (!userAlreadyExists)
                    {
                        users.Add(new User(clientEndPoint, "udp"));
                    }
                    User? user = users.Find(x => x.clientEndPoint == clientEndPoint);

                    if (user == null)
                    {
                        Console.WriteLine("ERR: No user found");
                        break;
                    }
                    HandleClientAsync(result, user);
                    watch.Stop();

                    Console.WriteLine($"Execution Time: {watch.ElapsedMilliseconds} ms");
                }
                catch (OperationCanceledException)
                {
                    //Console.WriteLine("Confirmation timed out");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERR " + ex.Message);
                }
            }
        }

        catch (Exception ex)
        {
            Console.WriteLine("ERR " + ex.Message);
        }
        finally
        {
            client.Close();
            Console.WriteLine("Server is shutting down.");
        }
    }
    public async Task HandleClientAsync(UdpReceiveResult messageBytes, User user)
    {
        startListinengToken.Cancel();
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
            SendConfirm(receivedMessageId, user);

            int replyMsgId = SendReply(receivedMessageId, user, "Auth success");
            int attempts = 0;
            if (!WaitConfirm(replyMsgId, user))
            {
                while (attempts < maxRetransmissions)
                {
                    replyMsgId = SendReply(receivedMessageId, user, "Auth success");
                    if (WaitConfirm(replyMsgId, user))
                    {
                        break;
                    }
                    else
                    {
                        if (attempts == maxRetransmissions - 1)
                        {
                            Console.WriteLine("ERR: No confirmation received");
                            return;
                        }
                        attempts++;
                    }
                }
            }
            //TODO send msg to all users in default chanel
            //for performance reasons
            Thread.Sleep(100);
            users.ForEach(x =>
                {
                    if (x.ChanelId == "default")
                    {
                        //for tcp users
                        x.SendMsgTcp("MSG FROM Server IS " + receivedDisplayName + " has joined default" + "\r\n", "default");
                        //for udp users
                        SendMsgUdp(receivedDisplayName + " has joined default", "Server", x);
                    }
                });
        }
        else if (msgType == MessageType.JOIN)
        {
            string receivedChanelId = ExtractString(serverResponse, startIndex: 3);
            string receivedDisplayName = ExtractString(serverResponse, startIndex: 3 + receivedChanelId.Length + 1);
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.ChanelId == user.ChanelId && x.Username != user.Username)
                    {
                        x.SendMsgTcp("MSG FROM Server IS " + receivedDisplayName + " has left " + user.ChanelId + "\r\n", user.ChanelId);
                        UdpServer.SendMsgUdp(receivedDisplayName + " has left " + user.ChanelId, "Server", x);
                    }
                });
            user.HandleJoin(receivedChanelId);
            Console.WriteLine("RECV " + user.clientEndPoint + " | JOIN");
            SendConfirm(receivedMessageId, user);
            int replyMsgId = SendReply(receivedMessageId, user, "Join success");
            int attempts = 0;
            if (!WaitConfirm(replyMsgId, user))
            {
                while (attempts < maxRetransmissions)
                {
                    replyMsgId = SendReply(receivedMessageId, user, "Join success");
                    if (WaitConfirm(replyMsgId, user))
                    {
                        break;
                    }
                    else
                    {
                        if (attempts == maxRetransmissions - 1)
                        {
                            Console.WriteLine("ERR: No confirmation received");
                            return;
                        }
                        attempts++;
                    }
                }
            }
            //for performance reasons
            Thread.Sleep(100);
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.ChanelId == user.ChanelId)
                    {
                        //for tcp users
                        x.SendMsgTcp("MSG FROM Server IS " + receivedDisplayName + " has joined " + receivedChanelId + "\r\n", receivedChanelId);
                        //for udp users
                        SendMsgUdp(receivedDisplayName + " has joined " + receivedChanelId, "Server", x);

                    }
                });
        }
        else if (msgType == MessageType.MSG)
        {
            string receivedDisplayName = ExtractString(serverResponse, startIndex: 3);
            string messageContents = ExtractString(serverResponse, startIndex: 3 + receivedDisplayName.Length + 1);
            Console.WriteLine("RECV " + user.clientEndPoint + " | MSG");
            SendConfirm(receivedMessageId, user);

            //Send to all users in the chanel
            //for performance reasons
            Thread.Sleep(100);
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.ChanelId == user.ChanelId && x.Username != user.Username)
                    {
                        //for tcp users
                        x.SendMsgTcp("MSG FROM " + receivedDisplayName + " IS " + messageContents + "\r\n", user.ChanelId);
                        //for udp users
                        SendMsgUdp(messageContents, receivedDisplayName, x);
                    }
                });
        }
        else if (msgType == MessageType.BYE)
        {
            //Console.WriteLine("Server has closed the connection.");
            Console.WriteLine("RECV " + user.clientEndPoint + " | BYE");
            SendConfirm(receivedMessageId, user);
            //Send to all users that user has left
            //for performance reasons
            Thread.Sleep(100);
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.Username != user.Username && x.ChanelId == user.ChanelId)
                    {
                        //for tcp users
                        x.SendMsgTcp("MSG FROM Server IS " + user.DisplayName + " has left the " + user.ChanelId + "\r\n", user.ChanelId);
                        //for udp users
                        UdpServer.SendMsgUdp(user.DisplayName + " has left the " + user.ChanelId, "Server", x);
                    }
                });


            users.Remove(user);
        }
        startListinengToken = new CancellationTokenSource();
    }

    public static bool WaitConfirm(int messageId, User user)
    {
        try
        {
            string clientIp = user.clientEndPoint.Split(':')[0];
            int clientPort = int.Parse(user.clientEndPoint.Split(':')[1]);
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(clientIp), clientPort);
            //also for performance reasons
            //Thread.Sleep(100);
            byte[] serverResponse = client.Receive(ref endpoint);

            byte[] responseMessageIdBytes = new byte[] { serverResponse[1], serverResponse[2] };
            if (endpoint.Address.ToString() != clientIp || endpoint.Port != clientPort)
            {
                return false;
            }
            // Check if the response is a "CONFIRM" message
            if (serverResponse[0] == (byte)MessageType.CONFIRM)
            {
                // Extract the message ID from the response
                if (BitConverter.ToInt16(responseMessageIdBytes, 0) != messageId)
                {
                    return false;
                }
                else
                {
                    Console.WriteLine("RECV " + user.clientEndPoint + " | CONFIRM");
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

    public static void SendConfirm(int messageId, User user)
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

    public static int SendReply(int replyMessageId, User user, string messageContent)
    {
        var messageBuilder = new UdpMessageBuilder();
        messageBuilder.AddMessageType((byte)MessageType.REPLY);
        int messageId = UdpMessageBuilder.GetMessageId();
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

    public static void SendErr(User user, string messageContent)
    {
        var messageBuilder = new UdpMessageBuilder();
        messageBuilder.AddMessageType((byte)MessageType.ERR);
        int messageId = UdpMessageBuilder.GetMessageId();
        messageBuilder.AddMessageId(messageId);
        messageBuilder.AddStringWithDelimiter(messageContent);
        byte[] message = messageBuilder.GetMessage();
        string clientIp = user.clientEndPoint.Split(':')[0];
        int clientPort = int.Parse(user.clientEndPoint.Split(':')[1]);
        client.Send(message, message.Length, clientIp, clientPort);
        int attempts = 0;
        if (!WaitConfirm(messageId, user))
        {
            while (attempts < maxRetransmissions)
            {
                messageId = UdpMessageBuilder.GetMessageId();
                UdpMessageBuilder.ReplaceMessageId(message, messageId);
                client.Send(message, message.Length, clientIp, clientPort);
                if (WaitConfirm(messageId, user))
                {
                    Console.WriteLine("SENT " + user.clientEndPoint + " | ERR");
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
            Console.WriteLine("SENT " + user.clientEndPoint + " | ERR");
        }
    }


    public static void SendMsgUdp(string msgContent, string receivedDisplayName, User user)
    {
        if (user.Protocol != "udp")
        {
            return;
        }
        startListinengToken.Cancel();
        var messageBuilder = new UdpMessageBuilder();
        messageBuilder.AddMessageType((byte)MessageType.MSG);
        int messageId = UdpMessageBuilder.GetMessageId();
        messageBuilder.AddMessageId(messageId);
        messageBuilder.AddStringWithDelimiter(receivedDisplayName);
        messageBuilder.AddStringWithDelimiter(msgContent);
        byte[] messageToSend = messageBuilder.GetMessage();
        string clientIp = user.clientEndPoint.Split(':')[0];
        int clientPort = int.Parse(user.clientEndPoint.Split(':')[1]);
        client.Send(messageToSend, messageToSend.Length, clientIp, clientPort);

        int attempts = 0;
        Console.WriteLine("SENT " + user.clientEndPoint + " | MSG");
        if (!WaitConfirm(messageId, user))
        {
            while (attempts < maxRetransmissions)
            {
                messageId = UdpMessageBuilder.GetMessageId();
                UdpMessageBuilder.ReplaceMessageId(messageToSend, messageId);
                client.Send(messageToSend, messageToSend.Length, clientIp, clientPort);
                if (WaitConfirm(messageId, user))
                {
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
        startListinengToken = new CancellationTokenSource();
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