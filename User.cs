using System.Net.Sockets;
using System.Xml.Serialization;

public class User
{

    public string? Username { get; set; }
    public string clientEndPoint { get; set; }
    public string Protocol { get; set; }
    public string? Secret { get; set; }
    public string? DisplayName { get; set; }
    public string? ChanelId { get; set; }
    //if tcp user
    public NetworkStream? stream { get; set; }

    public User(string clientEndPoint, string Protocol)
    {
        this.clientEndPoint = clientEndPoint;
        this.Protocol = Protocol;
    }
    public void AddStream(NetworkStream stream)
    {
        this.stream = stream;
    }
    public void HandleAuth(string Username, string DisplayName, string Secret)
    {
        this.Username = Username;
        this.DisplayName = DisplayName;
        this.Secret = Secret;
    }
    public void HandleJoin(string ChanelId)
    {
        this.ChanelId = ChanelId;
    }

    public void HandleMsg(string Msg)
    {
        if (Protocol == "tcp")
        {
            Console.WriteLine("TCP: " + Msg);
        }
    }

    public void ChangeDisplayName(string DisplayName)
    {
        this.DisplayName = DisplayName;
    }

    public void SendMsgTcp(string Msg, string chanelId)
    {
        if (Protocol == "tcp")
        {

            if (chanelId == ChanelId)
            {
                byte[] data = System.Text.Encoding.ASCII.GetBytes(Msg);
                stream.Write(data, 0, data.Length);
            }
        }
    }
    
    

}