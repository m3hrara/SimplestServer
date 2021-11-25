using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5478;
    LinkedList<PlayerAccount> playerAccounts;
    const int playerAccountRecord = 1;
    string playerAccountsFilePath;
    int playerWaitingForMatchWithID = -1;
    List<GameRoom> gameRooms;
    int turn = 1;
    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);
        playerAccountsFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt";
        playerAccounts = new LinkedList<PlayerAccount>();
        LoadPlayerAccounts();

        gameRooms = new List<GameRoom>();
    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }
  
    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);
        string[] csv = msg.Split(',');
        int Signifier = int.Parse(csv[0]);
        if (Signifier == ClientToServerSignifier.CreateAccount)
        {
            string n = csv[1];
            string p = csv[2];
            bool nameInUse = false;
            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    nameInUse = true;
                    break;
                }
            }
            if (nameInUse)
            {
                SendMessageToClient(ServerToClientSignifier.AccountCreationFailed + "", id);
            }
            else
            {
                PlayerAccount newPlayerAccount = new PlayerAccount(n, p);
                playerAccounts.AddLast(newPlayerAccount);
                SendMessageToClient(ServerToClientSignifier.AccountCreationComplete + "", id);
                Debug.Log("account creation complete");
                SavePlayerAccount();
            }
        }
        else if (Signifier == ClientToServerSignifier.Login)
        {
            bool nameFound = false;
            bool msgBeenSentToClient = false;
            string n = csv[1];
            string p = csv[2];
            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    nameFound = true;
                    if (pa.password == p)
                    {
                        SendMessageToClient(ServerToClientSignifier.LoginComplete + "", id);
                        Debug.Log("login complete");

                        msgBeenSentToClient = true;
                    }
                    else
                    {
                        SendMessageToClient(ServerToClientSignifier.LoginFailed + "", id);
                        msgBeenSentToClient = true;
                    }
                }
            }
            if (!nameFound)
            {
                if (!msgBeenSentToClient)
                {
                    SendMessageToClient(ServerToClientSignifier.LoginFailed + "", id);
                }
            }
        }
        else if (Signifier == ClientToServerSignifier.JoinQueueForGame)
        {
            if (gameRooms.Count == 0)
            {
                if (playerWaitingForMatchWithID == -1)
                {
                    playerWaitingForMatchWithID = id;
                }
                else
                {
                    GameRoom gr = new GameRoom(playerWaitingForMatchWithID, id);
                    gameRooms.Add(gr);
                    SendMessageToClient(ServerToClientSignifier.GameStart + "", gr.playerID1);
                    SendMessageToClient(ServerToClientSignifier.GameStart + "", gr.playerID2);

                    playerWaitingForMatchWithID = -1;
                }
            }
            else
            {
                gameRooms[Random.Range(0, gameRooms.Count)].ObserverID.Add(id);
                GameRoom gr = GetGameRoomWithClientID(id);
                SendMessageToClient(ServerToClientSignifier.StartObserving + "", id);

            }
        }

        else if (Signifier == ClientToServerSignifier.QuickChatOne)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatOneRecieved + "", gr.playerID2);
                    SendMessageToClient(ServerToClientSignifier.QuickChatOneSent + "", gr.playerID1);
                    if (gr.ObserverID.Count > 0)
                        SendMessageToClient(ServerToClientSignifier.QuickChatOneRecieved + "", gr.ObserverID[0]);

                }
                else if(gr.playerID2 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatOneRecieved + "", gr.playerID1);
                    SendMessageToClient(ServerToClientSignifier.QuickChatOneSent + "", gr.playerID2);
                    if (gr.ObserverID.Count > 0)

                        SendMessageToClient(ServerToClientSignifier.QuickChatOneRecieved + "", gr.ObserverID[0]);

                }
            }
        }
        else if (Signifier == ClientToServerSignifier.QuickChatTwo)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatTwoRecieved + "", gr.playerID2);
                    SendMessageToClient(ServerToClientSignifier.QuickChatTwoSent + "", gr.playerID1);
                    if (gr.ObserverID.Count > 0)

                        SendMessageToClient(ServerToClientSignifier.QuickChatTwoRecieved + "", gr.ObserverID[0]);

                }
                else if (gr.playerID2 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatTwoRecieved + "", gr.playerID1);
                    SendMessageToClient(ServerToClientSignifier.QuickChatTwoSent + "", gr.playerID2);
                    if (gr.ObserverID.Count > 0)

                        SendMessageToClient(ServerToClientSignifier.QuickChatTwoRecieved + "", gr.ObserverID[0]);

                }
            }
        }
        else if (Signifier == ClientToServerSignifier.QuickChatThree)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatThreeRecieved + "", gr.playerID2);
                    SendMessageToClient(ServerToClientSignifier.QuickChatThreeSent + "", gr.playerID1);
                    if (gr.ObserverID.Count > 0)

                        SendMessageToClient(ServerToClientSignifier.QuickChatThreeRecieved + "", gr.ObserverID[0]);

                }
                else if (gr.playerID2 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatThreeRecieved + "", gr.playerID1);
                    SendMessageToClient(ServerToClientSignifier.QuickChatThreeSent + "", gr.playerID2);
                    if (gr.ObserverID.Count > 0)

                        SendMessageToClient(ServerToClientSignifier.QuickChatThreeRecieved + "", gr.ObserverID[0]);

                }
            }
        }
        else if (Signifier == ClientToServerSignifier.SendMessage)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            SendMessageToClient(ServerToClientSignifier.TextMessage + "," + csv[1] + ":" + csv[2], gr.playerID1);
            SendMessageToClient(ServerToClientSignifier.TextMessage + "," + csv[1] + ":" + csv[2], gr.playerID2);
            if (gr.ObserverID.Count > 0)

                SendMessageToClient(ServerToClientSignifier.TextMessage + "," + csv[1] + ":" + csv[2], gr.ObserverID[0]);


        }
        else if (Signifier == ClientToServerSignifier.SendButtonOne)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (turn == 1 && gr.playerID1 == id)
            {
                    turn = 2;
                SendMessageToClient(ServerToClientSignifier.SlotOneX + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotOneX + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotOneX + "", gr.ObserverID[0]);

            }
            else if (turn == 2 && gr.playerID2 == id)
            {
                    turn = 1;
                SendMessageToClient(ServerToClientSignifier.SlotOneO + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotOneO + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotOneO + "", gr.ObserverID[0]);

            }

        }
        else if (Signifier == ClientToServerSignifier.SendButtonTwo)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (turn == 1 && gr.playerID1 == id)
            {
                turn = 2;

                SendMessageToClient(ServerToClientSignifier.SlotTwoX + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotTwoX + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotTwoX + "", gr.ObserverID[0]);

            }
            else if (turn == 2 && gr.playerID2 == id)
            {
                turn = 1;

                SendMessageToClient(ServerToClientSignifier.SlotTwoO + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotTwoO + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotTwoO + "", gr.ObserverID[0]);

            }

        }
        else if (Signifier == ClientToServerSignifier.SendButtonThree)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (turn == 1 && gr.playerID1 == id)
            {
                turn = 2;

                SendMessageToClient(ServerToClientSignifier.SlotThreeX + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotThreeX + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotThreeX + "", gr.ObserverID[0]);

            }
            else if (turn == 2 && gr.playerID2 == id)
            {
                turn = 1;

                SendMessageToClient(ServerToClientSignifier.SlotThreeO + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotThreeO + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotThreeO + "", gr.ObserverID[0]);

            }

        }
        else if (Signifier == ClientToServerSignifier.SendButtonFour)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (turn == 1 && gr.playerID1 == id)
            {
                turn = 2;

                SendMessageToClient(ServerToClientSignifier.SlotFourX + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotFourX + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotFourX + "", gr.ObserverID[0]);

            }
            else if (turn == 2 && gr.playerID2 == id)
            {
                turn = 1;

                SendMessageToClient(ServerToClientSignifier.SlotFourO + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotFourO + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotFourO + "", gr.ObserverID[0]);

            }

        }
        else if (Signifier == ClientToServerSignifier.SendButtonFive)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (turn == 1 && gr.playerID1 == id)
            {
                turn = 2;

                SendMessageToClient(ServerToClientSignifier.SlotFiveX + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotFiveX + "", gr.playerID2);
                if(gr.ObserverID.Count>0)
                SendMessageToClient(ServerToClientSignifier.SlotFiveX + "", gr.ObserverID[0]);

            }
            else if (turn == 2 && gr.playerID2 == id)
            {
                turn = 1;

                SendMessageToClient(ServerToClientSignifier.SlotFiveO + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotFiveO + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotFiveO + "", gr.ObserverID[0]);

            }

        }
        else if (Signifier == ClientToServerSignifier.SendButtonSix)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (turn == 1 && gr.playerID1 == id)
            {
                turn = 2;

                SendMessageToClient(ServerToClientSignifier.SlotSixX + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotSixX + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotSixX + "", gr.ObserverID[0]);

            }
            else if (turn == 2 && gr.playerID2 == id)
            {
                turn = 1;

                SendMessageToClient(ServerToClientSignifier.SlotSixO + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotSixO + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotSixO + "", gr.ObserverID[0]);

            }

        }
        else if (Signifier == ClientToServerSignifier.SendButtonSeven)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (turn == 1 && gr.playerID1 == id)
            {
                turn = 2;

                SendMessageToClient(ServerToClientSignifier.SlotSevenX + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotSevenX + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotSevenX + "", gr.ObserverID[0]);

            }
            else if (turn == 2 && gr.playerID2 == id)
            {
                turn = 1;

                SendMessageToClient(ServerToClientSignifier.SlotSevenO + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotSevenO + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotSevenO + "", gr.ObserverID[0]);

            }

        }
        else if (Signifier == ClientToServerSignifier.SendButtonEight)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (turn == 1 && gr.playerID1 == id)
            {
                turn = 2;

                SendMessageToClient(ServerToClientSignifier.SlotEightX + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotEightX + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotEightX + "", gr.ObserverID[0]);

            }
            else if (turn == 2 && gr.playerID2 == id)
            {
                turn = 1;

                SendMessageToClient(ServerToClientSignifier.SlotEightO + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotEightO + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotEightO + "", gr.ObserverID[0]);

            }

        }
        else if (Signifier == ClientToServerSignifier.SendButtonNine)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (turn == 1 && gr.playerID1 == id)
            {
                turn = 2;

                SendMessageToClient(ServerToClientSignifier.SlotNineX + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotNineX + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotNineX + "", gr.ObserverID[0]);

            }
            else if (turn == 2 && gr.playerID2 == id)
            {
                turn = 1;

                SendMessageToClient(ServerToClientSignifier.SlotNineO + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotNineO + "", gr.playerID2);
                if (gr.ObserverID.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotNineO + "", gr.ObserverID[0]);

            }

        }
    }
    public void SavePlayerAccount()
    {
        StreamWriter sw = new StreamWriter(playerAccountsFilePath);
        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(playerAccountRecord + ","+pa.name+","+pa.password);
        }
        sw.Close();
    }
    public void LoadPlayerAccounts()
    {
        if(File.Exists(playerAccountsFilePath))
        {
            StreamReader sr = new StreamReader(playerAccountsFilePath);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');
                int Signifier = int.Parse(csv[0]);
                if (Signifier == playerAccountRecord)
                {
                    PlayerAccount pa = new PlayerAccount(csv[1], csv[2]);
                    playerAccounts.AddLast(pa);
                }
            }
            sr.Close();
        }
    }
    private GameRoom GetGameRoomWithClientID(int id)
    {
        foreach (GameRoom gr in gameRooms)
        {
            if (gr.playerID1 == id || gr.playerID2 == id)
                return gr;
        }
        return null;
    }
}
public static class ClientToServerSignifier
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int JoinQueueForGame = 3;
    public const int TicTacToeSomethingPlay = 4;
    public const int QuickChatOne = 5;
    public const int QuickChatTwo = 6;
    public const int QuickChatThree = 7;
    public const int SendMessage = 8;
    public const int SendButtonIndex = 9;
    public const int SendButtonOne = 10;
    public const int SendButtonTwo = 11;
    public const int SendButtonThree = 12;
    public const int SendButtonFour = 13;
    public const int SendButtonFive = 14;
    public const int SendButtonSix = 15;
    public const int SendButtonSeven = 16;
    public const int SendButtonEight = 17;
    public const int SendButtonNine = 18;
}
public static class ServerToClientSignifier
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;
    public const int AccountCreationComplete = 3;
    public const int AccountCreationFailed = 4;
    public const int GameStart = 5;
    public const int OpponentPlay = 6;
    public const int QuickChatOneRecieved = 7;
    public const int QuickChatTwoRecieved = 8;
    public const int QuickChatThreeRecieved = 9;
    public const int QuickChatOneSent = 10;
    public const int QuickChatTwoSent = 11;
    public const int QuickChatThreeSent = 12;
    public const int TextMessage = 13;
    public const int SlotOneX = 14;
    public const int SlotOneO = 15;
    public const int SlotTwoX = 16;
    public const int SlotTwoO = 17;
    public const int SlotThreeX = 18;
    public const int SlotThreeO = 19;
    public const int SlotFourX = 20;
    public const int SlotFourO = 21;
    public const int SlotFiveX = 22;
    public const int SlotFiveO = 23;
    public const int SlotSixX = 24;
    public const int SlotSixO = 25;
    public const int SlotSevenX = 26;
    public const int SlotSevenO = 27;
    public const int SlotEightX = 28;
    public const int SlotEightO = 29;
    public const int SlotNineX = 30;
    public const int SlotNineO = 31;
    public const int StartObserving = 32;

}
public class PlayerAccount
{
    public string name, password;
    public PlayerAccount(string name, string password)
    {
        this.name = name;
        this.password = password;
    }
}

public class GameRoom
{
    public int playerID1, playerID2;
    public List<int> ObserverID;
    public GameRoom(int PlayerID1, int PlayerID2)
    {
        playerID1 = PlayerID1;
        playerID2 = PlayerID2;
        ObserverID = new List<int>();
    }
}