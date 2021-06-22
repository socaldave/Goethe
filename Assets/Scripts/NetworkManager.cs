using Firesplash.UnityAssets.SocketIO;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    public SocketIOCommunicator sioCom;

    
    //public Text uiStatus, uiPodName;
    public GameObject uUserPrefab;
    public Transform customerContentPanel;
    public Transform sellerContentPanel;
    public Transform serverContentPanel;
    public string serverName;
    public string room;
    private static ArrayList SellerStorage = new ArrayList();
    private static ArrayList CustomerStorage = new ArrayList();
    private static ArrayList ServerStorage = new ArrayList();
    private static ArrayList myGroup = new ArrayList();
    private Customer myServer;
    private Customer mySeller;
    private Customer myCustomer;
    public bool connected;
    // Start is called before the first frame update
    void Awake()
    {
        serverName = "server_" + UnityEngine.Random.Range(0, 9999);
        PlayerPrefs.SetString("room", serverName);
    }
    void Start()
    {
       
        //sioCom is assigned via inspector so no need to initialize it.
        //We just fetch the actual Socket.IO instance using its integrated Instance handle and subscribe to the connect event
        sioCom.Instance.On("connect", (string data) => {
            Debug.Log("LOCAL: Hey, we are connected!");
        });

     

        //When the conversation is done, the server will close out connection.
        sioCom.Instance.On("disconnect", (string payload) => {
            Debug.Log("Disconnected from server.");
           // uiStatus.text = "Finished. Server closed connection.";
        });


        sioCom.Instance.On("message", (string data) =>
        {
            MessageJSON messageJSON = MessageJSON.CreateFromJSON(data);
            Debug.Log("OnMessage data " + data);
            Debug.Log("messageJSON.type " + messageJSON.type);
       
            MessageComandJSON messageComandJSON = null;
            switch (messageJSON.type)
            {
                case "new_user_login":
                    messageComandJSON = MessageComandJSON.CreateFromJSON(data);
                    Debug.Log("new_user_login " + messageComandJSON.data);
                    sioCom.Instance.Emit("getRoomClients");
                   
                    break;
                case "user_left":
                    messageComandJSON = MessageComandJSON.CreateFromJSON(data);
                    string user = messageComandJSON.data;
                    Debug.Log("user " + user);
                    for (int i = 0; i < myGroup.Count; i++)
                    {
                        Debug.Log("user " + user);
             
                        if (myGroup[i].Equals(user))
                        {
                            if (user.Contains("customer"))
                            {

                                Debug.Log("myGroup[i] " + myGroup[i]);
                                myGroup.Clear();
                                connected = false;
                               
                               
                                /// webrtcControler.ShutdownButtonPressed();
                            }
                            if (messageComandJSON.data.Contains("seller"))
                            {
                                myGroup.RemoveAt(i);
                            }
                        }
                    }
                    Debug.Log("myGroup.Count " + myGroup.Count);
                    sioCom.Instance.Emit("getRoomClients");
                    break;
                case "get_user_list":
                    MessageUserListJSON messageUserListJSON = MessageUserListJSON.CreateFromJSON(data);
                    Debug.Log("get_user_list " + messageUserListJSON.data);
                    UpdateUserList(messageUserListJSON.data);
                    break;
                case "get_message":

                  
                    ChatJSON myObject =  JsonUtility.FromJson<ChatJSON>(data);
                    Debug.Log("nickname " + myObject.type);
                    Debug.Log("nickname " + myObject.data.nickname);
                    Debug.Log("message " + myObject.data.message);


                    //Debug.Log("message " + chatJSON.message.ToString());
                    break;

            }
        });

        //We are now ready to actually connect
       
        sioCom.Instance.Connect();
        
        JoinGame();
    }

    public void JoinGame()
    {
        StartCoroutine(ConnectToServer());
        StartCoroutine(WaitToServer());
    }


    public void SendMessage(string msg)
    {
        sioCom.Instance.Emit("sendMessage",msg, false);
        Debug.Log("sendMessage " + msg);
    }

    IEnumerator ConnectToServer()
    {
        yield return new WaitForSeconds(1f);

        LoginJSON loginJSON = new LoginJSON(serverName, room);
        string logdata = JsonUtility.ToJson(loginJSON);
        sioCom.Instance.Emit("login", logdata);
      

        Debug.Log("playerName" + serverName);
        Debug.Log("data" + logdata);
        yield return new WaitForSeconds(1f);
        sioCom.Instance.Emit("getRoomClients");
    }

    IEnumerator WaitToServer()
    {
        yield return new WaitForSeconds(5f);
        if(!sioCom.Instance.IsConnected())
        {
            sioCom.Instance.Connect();
            JoinGame();
            Debug.Log("WaitToServer");
        }
        StartCoroutine(WaitToServer());
    }
    public void UpdateUserList(string[] data)
    {
        Debug.Log("UpdateUserList " + data.Length);
        if (customerContentPanel != null)
        {
            SellerStorage.Clear();
            CustomerStorage.Clear();
            ServerStorage.Clear();
       
            foreach (Transform child in customerContentPanel)
            {
                GameObject.Destroy(child.gameObject);
            }
            foreach (Transform child in serverContentPanel)
            {
                GameObject.Destroy(child.gameObject);
            }
            foreach (Transform child in sellerContentPanel)
            {
                GameObject.Destroy(child.gameObject);
            }
            for (int i = 0; i < data.Length; i++)
            {
                GameObject newUserButton = Instantiate(uUserPrefab) as GameObject;
                UserButton button = newUserButton.GetComponent<UserButton>();

                button.buttonUser.onClick.AddListener(delegate { OnUserClick(button); });
                button.name = data[i];
                button.nameLabel.text = data[i];

                if (data[i].Contains("server"))
                {
                    button.buttonUser.onClick.AddListener(delegate { OnUserClick(button); });
                    newUserButton.transform.SetParent(serverContentPanel, false);
                    Customer newServer = new Customer();
                    newServer.room = data[i];
                    ServerStorage.Add(newServer);
                }

                if (data[i].Contains("customer"))
                {
                    button.buttonUser.onClick.AddListener(delegate { OnUserClick(button); });
                    newUserButton.transform.SetParent(customerContentPanel, false);
                    Customer newCustomer = new Customer();
                    newCustomer.room = data[i];
                    CustomerStorage.Add(newCustomer);
                }

                if (data[i].Contains("seller"))
                {
                    newUserButton.transform.SetParent(sellerContentPanel, false);
                    Customer newSeller = new Customer();
                    newSeller.room = data[i];
                    SellerStorage.Add(newSeller);
                }
            }



             if (ServerStorage.Count > 0 && CustomerStorage.Count > 0)
             {
                for (int i = 0; i < ServerStorage.Count; i++)
                {
                    myServer = (Customer)ServerStorage[i];
                  
                    Debug.Log("myServer " + myServer.room);

                    if (myServer.room.Equals(serverName))
                    {

                       
                        if(CustomerStorage.Count > i)
                        {
                            Debug.Log("CustomerStorage.Count " + i);
                            myCustomer = (Customer)CustomerStorage[i];
                            Debug.Log("myGroup.Count " + myGroup.Count);
                            if (myGroup.Count == 0)
                            {
                                myGroup.Add(myServer.room);
                                myGroup.Add(myCustomer.room);
                                string msg = "connect|" + myServer.room + "|" + myCustomer.room;
                                SendMessage(msg);
                                PlayerPrefs.SetString("room", myServer.room);
                                connected = true;
                             

                                //webrtcControler.JoinButtonPressed();
                                //StartCoroutine(WaitForServer());
                            }
                        }
                    }
                }
               
                
            }

            if (ServerStorage.Count > 0 && SellerStorage.Count > 0 && CustomerStorage.Count > 0)
            {
              
                
                mySeller = (Customer)SellerStorage[0];
                if (myGroup.Count == 2)
                {
                    myGroup.Add(mySeller.room);
                    string msg = "connect|" + myGroup[0] + "|" + myGroup[1] + "|" + mySeller.room;
                    SendMessage(msg);
                    PlayerPrefs.SetString("room", myServer.room);
                    //StartCoroutine(WaitForServer());
                }
            }



        }
        else
        {
            //if(data.Length<2)
                //sioCom.Instance.Close();
                //SceneManager.LoadScene("FashionBaracudaServer");
        }
    }


    IEnumerator WaitForServer()
    {
        yield return new WaitForSeconds(2f);


        //SceneManager.LoadScene("FashionBaracudaServer");
       
       

    }

    void OnUserClick(UserButton button)
    {
    }

    private void OnDestroy()
    {
        sioCom.Instance.Close();
    }

    [Serializable]
    struct ItsMessage
    {
        public string message;
    }

    [Serializable]
    public class LoginJSON
    {
        public string nickname;
        public string room;


        public LoginJSON(string _name, string _room)
        {

            nickname = _name;
            room = _room;
        }
    }

    [Serializable]
    public class MessageJSON
    {
        public string type;

        public static MessageJSON CreateFromJSON(string type)
        {
            return JsonUtility.FromJson<MessageJSON>(type);
        }
    }

    [Serializable]
    public class MessageUserListJSON
    {
        public string type;
        public string[] data;

        public static MessageUserListJSON CreateFromJSON(string data)
        {
            return JsonUtility.FromJson<MessageUserListJSON>(data);
        }
    }

    [Serializable]
    public class MessageComandJSON
    {
        public string type;
        public string data;

        public static MessageComandJSON CreateFromJSON(string data)
        {
            return JsonUtility.FromJson<MessageComandJSON>(data);
        }
    }

    [Serializable]
    public class ChatJSON
    {
        public string type;
        public item data;

        public static ChatJSON CreateFromJSON(string data)
        {
            return JsonUtility.FromJson<ChatJSON>(data);
        }

    }

    [Serializable]
    public class item
    {
        public string nickname;
        public string message;
    }

    [Serializable]
    public class MsgJSON
    {
        public string message;
        public MsgJSON(string _msg)
        {
            message = _msg;
        }
    }
}
