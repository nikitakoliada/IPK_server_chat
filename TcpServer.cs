using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class TcpServer
{
    public string server;

    public int port;
    public List<User> users;

    public TcpServer(string server, int port, ref List<User> users)
    {
        this.server = server;
        this.port = port;
        this.users = users;

    }

    public async Task StartListening(CancellationTokenSource cts)
    {
        var listener = new TcpListener(IPAddress.Parse(server), port);
        listener.Start();

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                //Console.WriteLine("Waiting for a connection...");
                // Asynchronously wait for an incoming connection
                TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                _ = HandleClientAsync(client);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERR " + ex.Message);
        }
        finally
        {
            Console.WriteLine("Server is shutting down.");
            listener.Stop();
        }
    }



    public async Task HandleClientAsync(TcpClient client)
    {
        string? clientEndPoint = client.Client.RemoteEndPoint?.ToString();
        if (clientEndPoint == null)
        {
            Console.WriteLine("ERR: No client endpoint");
            return;
        }
        if (!users.Contains(new User(clientEndPoint, "tcp")))
        {
            users.Add(new User(clientEndPoint, "tcp"));
            //Console.WriteLine($"Saved new IP: {clientEndPoint}");
        }
        User? user = users.Find(x => x.clientEndPoint == clientEndPoint);
        if (user == null)
        {
            Console.WriteLine("ERR: No user found");
            return;
        }
        using (NetworkStream stream = client.GetStream())
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
        {
            try
            {
                user.AddStream(stream);
                string message;
                while ((message = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    HandleResponse(user, message);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("ERR: " + e.Message);
            }
        }
        client.Close();
    }

    public void HandleResponse(User user, string responseData)
    {
        // th is this lol

        if (responseData.Contains("AUTH"))
        {
            string pattern = @"AUTH (\S+) AS (\S+) USING (\S+)";
            Regex regex = new Regex(pattern);

            // Match the regular expression pattern against a text string
            Match match = regex.Match(responseData);

            if (match.Success)
            {
                // Extracted variables
                string username = match.Groups[1].Value;
                string displayName = match.Groups[2].Value;
                string secret = match.Groups[3].Value;
                Console.WriteLine("RECV " + user.clientEndPoint + " | AUTH");
                user.HandleAuth(username, displayName, secret);
                SendResponse("REPLY OK IS Auth success\r\n", user.stream, user);
                user.HandleJoin("default");
                //for performance reasons
                Thread.Sleep(100);
                users.ForEach(x =>
                {
                    if (x.ChanelId == "default")
                    {
                        //for tcp users
                        x.SendMsgTcp("MSG FROM Server IS " + displayName + " has joined default" + "\r\n", "default");
                        //for udp users
                        UdpServer.SendMsgUdp(displayName + " has joined default", "Server", x);
                    }
                });
            }
            else
            {
                SendResponse("ERR FROM Server IS Wrong auth format" + "\r\n", user.stream, user);
            }
        }
        else if (responseData.Contains("MSG"))
        {
            string pattern = @"MSG FROM (\S+) IS (.+)";
            Regex regex = new Regex(pattern);

            // Match the regular expression pattern against a text string
            Match match = regex.Match(responseData);

            if (match.Success)
            {
                string displayName = match.Groups[1].Value;
                string message = match.Groups[2].Value;
                Console.WriteLine("RECV " + user.clientEndPoint + " | MSG");

                users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.ChanelId == user.ChanelId && x.Username != user.Username)
                    {
                        x.SendMsgTcp("MSG FROM " + displayName + " IS " + message + "\r\n", user.ChanelId);
                        UdpServer.SendMsgUdp(message, displayName, x);
                    }
                });
            }
            else
            {
                SendResponse("ERR FROM Server IS Wrong msg format" + "\r\n", user.stream, user);
            }
        }
        else if (responseData.Contains("ERR"))
        {
            Console.WriteLine("RECV " + user.clientEndPoint + " | ERR");
            SendResponse("BYE", user.stream, user);
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.ChanelId == user.ChanelId && x.Username != user.Username)
                    {
                        x.SendMsgTcp("MSG FROM Server IS " + user.DisplayName + " has left the " + user.ChanelId + "\r\n", user.ChanelId);
                        UdpServer.SendMsgUdp(user.DisplayName + " has left the " + user.ChanelId, "Server", x);
                    }
                });
            users.Remove(user);

        }
        else if (responseData.Contains("JOIN"))
        {
            string pattern = @"JOIN (\S+) AS (\S+)";
            Regex regex = new Regex(pattern);

            // Match the regular expression pattern against a text string
            Match match = regex.Match(responseData);

            if (match.Success)
            {
                string chanelId = match.Groups[1].Value.Trim();
                string displayName = match.Groups[2].Value.Trim();
                Console.WriteLine("RECV " + user.clientEndPoint + " | JOIN");
                user.ChangeDisplayName(displayName);
                users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.ChanelId == user.ChanelId && x.Username != user.Username)
                    {
                        x.SendMsgTcp("MSG FROM Server IS " + displayName + " has left " + user.ChanelId + "\r\n", user.ChanelId);
                        UdpServer.SendMsgUdp(displayName + " has left " + user.ChanelId, "Server", x);
                    }
                });
                user.HandleJoin(chanelId);
                SendResponse("REPLY OK IS Join success", user.stream, user);
                //for performance reasons
                Thread.Sleep(100);
                users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.ChanelId == user.ChanelId)
                    {
                        x.SendMsgTcp("MSG FROM Server IS " + displayName + " has joined " + chanelId + "\r\n", chanelId);
                        UdpServer.SendMsgUdp(displayName + " has joined " + chanelId, "Server", x);
                    }
                });
            }
            else
            {
                SendResponse("ERR FROM Server IS Wrong msg format" + "\r\n", user.stream, user);
            }
        }
        else if (responseData.Contains("BYE"))
        {
            Console.WriteLine("RECV " + user.clientEndPoint + " | BYE");
            users.ForEach(x =>
                {
                    if (x.ChanelId != null && x.Username != user.Username && x.ChanelId == user.ChanelId)
                    {
                        x.SendMsgTcp("MSG FROM Server IS " + user.DisplayName + " has left the " + user.ChanelId + "\r\n", user.ChanelId);
                        UdpServer.SendMsgUdp(user.DisplayName + " has left the " + user.ChanelId, "Server", x);
                    }
                });
            users.Remove(user);
        }
        else
        {
            SendResponse("ERR FROM Server IS Unknown command", user.stream, user);
        }
    }
    public void SendResponse(string message, NetworkStream stream, User user)
    {
        Console.WriteLine("SENT " + user.clientEndPoint + " | REPLY");
        byte[] data = Encoding.ASCII.GetBytes(message);
        stream.Write(data, 0, data.Length);
    }
}