using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using System.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace ChatroomMainForm
{
    public class ClientInfo : IComparable<ClientInfo>
    {
        private string _IP = string.Empty;             //該AP連過來的IP
        private string _Name = string.Empty;           //AP名稱
        public SocketAsyncEventArgs Arg = null;
        public string PACKET_BUFFER = string.Empty;    //未完整接收的封包內容

        public string IP
        {
            get {return _IP; }
            set { _IP = value;}
        }

        public string Name
        {
            get { return _Name; }
            set { _Name = value; }
        }

        public int CompareTo(ClientInfo part)
        {
            if (part == null)
            {
                return 1;
            }
            else
            {
                return this.Name.CompareTo(part.Name);
            }
        }
    }

    public struct rMsgPack
    {
        public string ApName;
        public string Msg;
    }

    public class SocketServer
    {
        private int PORT;                    //監聽的port
        private int BUFFER_SIZE = 8192;      //接收資料的緩衝區大小(連線時域設)
        private Socket SERVER_SOCKET;        //負責監聽的socket物件
        private bool LISTENED = false;       //是否正在監聽
        private ConcurrentDictionary<string, ClientInfo> NEW_CLIENT = new ConcurrentDictionary<string,ClientInfo>();   // Key: IP
        private List<string> ALLOWCLIENT = new List<string>();
        MessageTrans msgTrans = new MessageTrans();    //server 與 client、client 與 client 間的訊息接收與傳送 (json)

        int MsgDicKey = 0;
        public ConcurrentDictionary<int, rMsgPack> MsgDic = new ConcurrentDictionary<int,rMsgPack>();

        /// <summary>
        /// 取得Clinet端資訊
        /// </summary>
        public ConcurrentDictionary<string, ClientInfo> GetUserInfo
        {
            get { return NEW_CLIENT; }
            set { NEW_CLIENT = value; }
        }

        //委派事件
        public delegate void clientMsgDelegate(string ip, string msg);   //委派(將客戶端訊息丟出  [時間,IP,訊息])
        public event clientMsgDelegate ClientMsgDelegate;

        public delegate void systemMsgDelegate(string time, string process, string ip, string msg);  //委派(將系統訊息丟出  [時間,事件流程,IP,訊息])
        public event systemMsgDelegate SystemMsgDelegate;

        public delegate void isListenDelegate(bool isListen);    //委派(將是否正在監聽訊息丟出)
        public event isListenDelegate IsListenDelegate;

        public delegate void userInfodelegate(ClientInfo aClientInfo, string status);   //委派(使用者資訊變更時丟出)
        public event userInfodelegate UserInfoDelegate;

        public delegate void clientMsgSendDelegate(string msg, string dealer);   //委派(將客戶端訊息丟出 [訊息])
        public event clientMsgSendDelegate ClientMsgSendDelegate;

        /// <summary>
        /// 建構子
        /// </summary>
        /// <param name="port">要監聽的prot</param>
        public SocketServer(int port)
        {
            this.PORT = port;
            NEW_CLIENT.Clear();
        }

        /// <summary>
        /// 監聽程序
        /// </summary>
        public void Listen()
        {
            try
            {
                SERVER_SOCKET = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);      //初始化Socket物件
                IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, PORT);                                   //監聽指定port的任何網路介面卡
                SERVER_SOCKET.Bind(localEndPoint);                                                                //與本機建立關連
                SERVER_SOCKET.Listen(1000);                                                                       //最大同時連接排隊數量
                StartAccept(null);                                                                                //開始與Client建立連接
                LISTENED = true;                                                                                  //開始監聽
                SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), "監聽程序時", "本機", "啟動完成");
            }
            catch (Exception e)
            {
                SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), "監聽程序時", "本機", string.Format("發生錯誤({0})", e.Message));
            }
            IsListenDelegate(LISTENED);     //委派(將是否完成監聽程序訊息丟出)
        }

        public void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            //判斷通訊端作業物件，若為空則建立並且綁定事件，若不為空則清除來重複使用
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);     //綁定事件
            }
            else
            {
                acceptEventArg.AcceptSocket = null;
            }

            if (!SERVER_SOCKET.AcceptAsync(acceptEventArg))    //有連線嘗試
            {
                ProcessAccept(acceptEventArg);
            }
        }

        //事件(與Clinet連接受執行)
        private void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        //與Client連接完成時
        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                SocketAsyncEventArgs receiveArg = new SocketAsyncEventArgs();                     //負責接收的通訊端作業
                receiveArg.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);     //綁定事件(傳送&接收)
                receiveArg.AcceptSocket = e.AcceptSocket;
                Socket sock = e.AcceptSocket;
                byte[] aBuffer = new byte[BUFFER_SIZE];
                receiveArg.SetBuffer(aBuffer, 0, aBuffer.Length);

                if (!sock.ReceiveAsync(receiveArg))
                {
                    ProcessReceive(receiveArg);
                }
            }
            else
            {
                ProcessError("與Client連接完成時", e);
            }

            //繼續聆聽
            if (LISTENED)
            {
                StartAccept(e);
            }
        }

        //依據SocketAsyncEventArgs狀態執行不同事件
        private void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;
                default:
                    throw new Exception("無效的處理事件");
            }
        }
        
        //接收來自Client資料
        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            //狀態為正常且接受訊息長度不為0，否則結束此Client的連接
            if (e.SocketError == SocketError.Success && e.BytesTransferred != 0)
            {
                Socket sock = e.AcceptSocket;
                byte[] aBuffer = new byte[e.BytesTransferred];
                Buffer.BlockCopy(e.Buffer, 0, aBuffer, 0, aBuffer.Length);
                string sMsg = Encoding.UTF8.GetString(aBuffer);

                ClientInfo aClient;
                string aCombineMsg;

                if (!NEW_CLIENT.TryGetValue(sock.RemoteEndPoint.ToString(), out aClient))
                {
                    aCombineMsg = sMsg;
                }
                else
                {
                    aCombineMsg = aClient.PACKET_BUFFER + sMsg;
                    aClient.PACKET_BUFFER = string.Empty;
                }

                aCombineMsg = aCombineMsg.Replace("��", ((char)0xF2).ToString());    //過去UTF8的REPLACEMENT CHARACTER
                string[] aMsg = aCombineMsg.Split((char)0xF2);
                int iLast = aMsg.Length - 1;
                for (int i = 0; i <= iLast; i++ )
                {
                    if(i == iLast)
                    {
                        if (sock != null && sock.Connected && NEW_CLIENT.TryGetValue(sock.RemoteEndPoint.ToString(), out aClient))
                        {
                            aClient.PACKET_BUFFER = aMsg[iLast];
                        }
                        continue;
                    }
                    else
                    {
                        UserInfo(aMsg[i], e);
                    }
                }

                //連線正常才繼續等待接收此連線的資料
                if (sock.Connected)
                {
                    if (!sock.ReceiveAsync(e))
                    {
                        ProcessReceive(e);
                    }
                }
            }
            else
            {
                ProcessError("接收來自Client資料時", e);
                if (LISTENED)
                {
                    CloseClientSocket(e);
                }
            }
        }

        /// <summary>
        /// 傳送給指定的單一Client
        /// </summary>
        /// <param name="msg">要傳送的字串</param>
        /// <param name="name">Client端名稱</param>
        public void SendToOneClient(string msg, string name)
        {
            List<ClientInfo> aClientAry = NEW_CLIENT.Values.ToList<ClientInfo>();
            int idx = aClientAry.FindIndex(x => x.Name == name);

            if ( idx != -1)
            {
                Send(msg, "傳送給指定的單一Client時", aClientAry[idx].Arg);
            }
            else
            {
                SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), "傳送給指定的單一Client時", name, "找不到指定的Client端");
            }
        }

        /// <summary>
        /// 傳送給指定的Client群組(單一條件)
        /// </summary>
        /// <param name="msg">要傳送的字串</param>
        /// <param name="groupName">Client群組名稱</param>
        public void SendToClientGroup(string msg, string groupName)
        {
            try
            {
                var aInfoAry = from entry in NEW_CLIENT
                               where entry.Value.Name.StartsWith(groupName)     // like "groupname%"
                               select entry.Value;
                ClientInfo aClientInfo;
                for (int i = 0; i < aInfoAry.Count(); i++ )
                {
                    aClientInfo = aInfoAry.ElementAt(i);
                    Send(msg, "傳送給指定的Client群組(多種條件)時", aClientInfo.Arg);
                }
            }
            catch
            {
                SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), "傳送給指定的Client群組(多種條件)時", "", "條件組合有誤，請檢查程式");
            }
        }

        /// <summary>
        /// 傳送給所有在線上的client
        /// </summary>
        /// <param name="msg">要傳送的字串</param>
        public void SendToAllClient(string msg)
        {
            foreach (var item in NEW_CLIENT)
            {
                Send(msg, "傳送給所有在線上的Client時", item.Value.Arg);
            }
        }

        //負責傳送的方法(要傳送的字串, 哪個流程觸發, Client通訊端作業物件)
        private void Send(string msg, string process, SocketAsyncEventArgs clientArg)
        {
            try
            {
                if (clientArg.SocketError == SocketError.Success)
                {
                    SocketAsyncEventArgs sendArg = new SocketAsyncEventArgs();
                    sendArg.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);

                    msg = msg + (char)0xF2;    //訊息結束固定加結束符號

                    byte[] aBuffer = Encoding.UTF8.GetBytes(msg);
                    sendArg.SetBuffer(aBuffer, 0, aBuffer.Length);
                    Socket sock = clientArg.AcceptSocket;

                    if (sock.Connected)
                    {
                        bool bWillRaiseEvent = true;
                        string sIP = "ip";

                        try
                        {
                            sIP = sock.RemoteEndPoint.ToString();
                        }
                        catch (Exception ex)
                        {
                            SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), "傳送時(1)", "", ex.Message);
                        }

                        try
                        {
                            sendArg.AcceptSocket = clientArg.AcceptSocket;
                        }
                        catch (Exception ex)
                        {
                            SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), "傳送時(2)", "", ex.Message);
                        }

                        try
                        {
                            bWillRaiseEvent = sock.SendAsync(sendArg);
                        }
                        catch (Exception ex)
                        {
                            SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), "傳送時(3)", "", ex.Message);
                        }
                        if (!bWillRaiseEvent)
                        {
                            ProcessSend(sendArg);
                        }
                    }
                }
                else
                {
                    ProcessError(process,clientArg);
                    CloseClientSocket(clientArg);
                }
            }
            catch (Exception ex)
            {
                SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), "傳送時", "", ex.Message);
            }
        }

        //傳送完成時
        private void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                ProcessError("傳送完成時", e);
            }
            else
            {
                e.Dispose();
            }
        }

        //使用者資訊處理
        private void UserInfo(string msg, SocketAsyncEventArgs clientArg)
        {
            Socket sock = clientArg.AcceptSocket;
            if (sock.Connected)
            {
                string sClientIP = sock.RemoteEndPoint.ToString();   //取得client端ip位址

                //若NEW_CLIENT裡找不到，表示此訊息為登入(檢查)，否則為單純字串傳送
                if (!NEW_CLIENT.ContainsKey(sClientIP))
                {
                    try
                    {
                        int iCount = NEW_CLIENT.Values.ToList<ClientInfo>().FindAll(x => x.Name == msg).Count;
                        if (iCount == 0)
                        {
                            //加入連線資訊
                            ClientInfo aClientInfo = new ClientInfo();
                            aClientInfo.Name = msg;                      //連線AP名
                            aClientInfo.IP = sClientIP;                  //存入Client端IP位址
                            aClientInfo.Arg = clientArg;                 //存入此Client端的通訊端作業
                            UserInfoDelegate(aClientInfo, "add");        //丟出要新增的列讓Form處理，有可能是因為UI跨執行緒的問題，不這麼做會導致DataGridView重畫畫面的時候發生錯誤
                            SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), "使用者登入", sClientIP, "登入完成");
                            //Send("true", "登入成功", clientArg);
                        }
                        else
                        {
                            string catchMsg = msgTrans.MessageCombine("repeat", "server", "self", "已有相同暱稱使用者登入");
                            Send(catchMsg, "重複登入", clientArg);
                            CloseClientSocket(clientArg);
                        }
                    }
                    catch (Exception ex)
                    {
                        SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), "接收字串錯誤:" + msg, sClientIP, ex.Message);
                    }
                }
                else if ( msg == "{\"action\":\"speedTest\"}")   //轉接器測速
                {
                    Send(msg, "測速", clientArg);
                }else
                {
                    ClientInfo aClientInfo;
                    if (NEW_CLIENT.TryGetValue(sClientIP, out aClientInfo))
                    {
                        rMsgPack aMsg = new rMsgPack();
                        aMsg.ApName = aClientInfo.Name;
                        aMsg.Msg = msg;
                        if (MsgDicKey == int.MaxValue)
                        {
                            MsgDicKey = 0;
                        }
                        MsgDic.TryAdd(++MsgDicKey, aMsg);

                        WaitCallback myWaitCallBack = new WaitCallback(ClientMsgSendDelegate_Thread);
                        ThreadPool.QueueUserWorkItem(myWaitCallBack, MsgDicKey);
                    }
                    else
                    {
                        SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), "非登入會員傳送資料:", sClientIP, msg);
                    }
                }
            }
        }

        //移除Client端
        private void CloseClientSocket(SocketAsyncEventArgs e)
        {
            Socket sock = e.AcceptSocket;
            string sIP = sock.RemoteEndPoint.ToString();
            string sName = string.Empty;

            if (e.SocketError == SocketError.Success)
            {
                sock.Shutdown(SocketShutdown.Both);   //暫停作業
                System.Threading.Thread.Sleep(10);
                sock.Close();                         //關閉物件
                sock.Dispose();                       //釋放資源
                e.Dispose(); 
            }

            ClientInfo aClientInfo;
            if (NEW_CLIENT.TryGetValue(sIP, out aClientInfo))
            {
                sName = aClientInfo.Name;
                UserInfoDelegate(aClientInfo, "del");
            }
            SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), "釋放與Client端的連線", sName, "釋放完成");
        }

        /// <summary>
        /// 關閉監聽
        /// </summary>
        public void Disconnect()
        {
            LISTENED = false;
            SERVER_SOCKET.Close();
            SERVER_SOCKET.Dispose();

            //關閉與釋放所有Client端的Socket
            foreach (var item in NEW_CLIENT)
            {
                Socket sock = item.Value.Arg.AcceptSocket;
                sock.Shutdown(SocketShutdown.Both);
                sock.Close();
                sock.Dispose();
                item.Value.Arg.Dispose();
            }
            UserInfoDelegate(null, "clear");   //丟出要清空訊息讓Form處理，有可能是因為UI跨執行緒的問題，不這麼做會導致DataGridView重畫畫面的時後發生錯誤
            IsListenDelegate(LISTENED);        //委派(將是否連正在監聽的訊息丟出)
        }
        private void ProcessError(string process, SocketAsyncEventArgs error)
        {
            string sMsg = string.Empty;
            Socket sock = error.AcceptSocket;
            if (sock.Connected)
            {
                string sIP = sock.RemoteEndPoint == null ? string.Empty : sock.RemoteEndPoint.ToString();
                switch (error.SocketError)
                {
                    case SocketError.ConnectionReset:      //連線重設時
                    case SocketError.Success:              //Client端關閉連線時會收到0，特別處理
                        sMsg = "客戶端端離線";
                        break;
                    case SocketError.ConnectionRefused:
                        sMsg = "客戶端拒絕連線，可能是未開啟";
                        break;
                    case SocketError.TimedOut:
                        sMsg = "連線嘗試逾時，或客戶端無法回應";
                        break;
                    case SocketError.OperationAborted:
                        sMsg = "離線";
                        break;
                    case SocketError.ConnectionAborted:
                        sMsg = "連線已由 .Net Framework 或基礎通訊端提供者終止";
                        break;
                    default:
                        sMsg = string.Format("Socket.Error( {0} )", error.SocketError);
                        break;
                }
                SystemMsgDelegate(DateTime.Now.ToString("HH:mm:ss"), process, sIP, sMsg);
            }
        }

        //執行緒處理
        private void ClientMsgSendDelegate_Thread(object aMsgKey)
        {
            int aMsgDicKey = Convert.ToInt32(aMsgKey);
            rMsgPack aMsgPack;
            if (!MsgDic.TryRemove(aMsgDicKey, out aMsgPack))
            {
                return;
            }
            ClientMsgSendDelegate(aMsgPack.Msg, aMsgPack.ApName);
        }
    }
}
