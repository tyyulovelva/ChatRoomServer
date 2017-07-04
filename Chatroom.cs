using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Concurrent;
using System.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace ChatroomMainForm
{
    public partial class frmMain : Form
    {
        private SocketServer SERVER = null;                            //監聽Client物件
        private ConcurrentDictionary<string, ClientInfo> NEW_CLIENT;   //轉接器資料表
        private bool LISTENED = false;                                 //是否正在監聽
        private int AP_COUNT = 0;                                      //連線過來的AP數量
        MessageTrans msgTrans = new MessageTrans();    //server 與 client、client 與 client 間的訊息接收與傳送 (json)

        public Logger logger = LogManager.GetCurrentClassLogger();
        //private string
        //private System.Threading.Timer TMR_HEART;                      //心跳包

        public frmMain()
        {
            InitializeComponent();

            //TMR_HEART = new System.Threading.Timer(new System.Threading.TimerCallback(Heart), null, -1, -1);
        }
        //心跳包
        private void Heart(object state)
        {
            if (SERVER != null) {
                SERVER.SendToAllClient(this.txtSendToClient.Text);
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            SERVER = new SocketServer((int)this.numericPort.Value);       //初始化監聽Client端物件
            SERVER.ClientMsgDelegate += this.ClientMsg;                   //綁定事件(Client訊息)
            SERVER.SystemMsgDelegate += this.SystemMsg;                   //綁定事件(系統訊息或錯誤訊息)
            SERVER.IsListenDelegate += this.IsListen;                     //綁定事件(是否監聽)
            SERVER.UserInfoDelegate += this.ClientInfo;
            SERVER.ClientMsgSendDelegate += this.ClientMsgSendDeal;       //綁定事件(client端使用者傳送訊息時，傳送給中央server)
            NEW_CLIENT = SERVER.GetUserInfo;

            List<ClientInfo> temp = NEW_CLIENT.Values.ToList<ClientInfo>();

            //string users = string.Empty;
            //foreach (ClientInfo x in temp)
            //{
            //    users += x.Name + "#";   //取得目前有登入的user
            //}

            temp.Sort();
            this.dgvClient.DataSource = temp;
            
            SERVER.Listen();                                              //開始監聽
            //TMR_HEART.Change(20000,20000);                                //心跳包 20秒
            this.btnStart.Enabled = false;
        }

        //與Client端所有訊息 觸發事件
        private void ClientMsg(string ip, string msg)
        {
            
            string sName = ip;
            if (ip.IndexOf(":") > 0)
            {
                ClientInfo aClientInfo;
                if (NEW_CLIENT.TryGetValue(ip, out aClientInfo))
                {
                    sName = aClientInfo.Name;
                }
                this.BeginInvoke(new ShowClientMsgDe(ShowClientMsg), new object[] { sName, msg } );
            }
        }

        //顯示與Client的相關訊息
        delegate void ShowClientMsgDe(string ip, string msg);
        private void ShowClientMsg(string ip, string msg)
        {
            if (this.txtClientLog.Lines.Length > 500)
            {
                this.txtClientLog.Text = "";
            }
            this.txtClientLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "--" + " " + ip + ":" + msg + "\r\n");
            logger.Info(DateTime.Now.ToString("HH:mm:ss") + "--" + " " + ip + ":" + msg + "\r\n");
        }

        //系統訊息或錯誤訊息 觸發事件
        private void SystemMsg(string time, string process, string aApName, string msg)
        {
            this.BeginInvoke(new ShowSystemMsgDe(ShowSystemMsg), new object[] { time, process, aApName, msg});
        }

        //顯示與系統的相關訊息
        delegate void ShowSystemMsgDe(string time, string process, string aApName, string msg);
        private void ShowSystemMsg(string time, string process, string aApName, string msg)
        {
            if (this.txtClientLog.Lines.Length > 500)
            {
                this.txtClientLog.Text = "";
            }
            this.txtClientLog.AppendText(time + "--" + process + " " + aApName + ":" + msg + "\r\n");
            logger.Info(time + "--" + process + " " + aApName + ":" + msg + "\r\n");
        }

        //接收是否正在監聽
        private void IsListen(bool isListen)
        {
            LISTENED = isListen;
        }

        private void ClientInfo(ClientInfo aClientInfo, string status)
        {
            this.BeginInvoke(new RefreshClientInfoDe(RefreshClientInfoMsg), new object[] { aClientInfo, status});
        }
        //更新使用者列表畫面
        delegate void RefreshClientInfoDe(ClientInfo aClientInfo, string status);
        private void RefreshClientInfoMsg(ClientInfo aClientInfo, string status)
        {
            string catchMsg = string.Empty;
            string userList = string.Empty;
            switch (status)
            {
                case "add":
                    NEW_CLIENT.TryAdd(aClientInfo.IP, aClientInfo);
                    AP_COUNT++;
                    catchMsg = msgTrans.MessageCombine("login", "server", aClientInfo.Name, " 已進入聊天");
                    break;
                case "del":
                    DeleteReference(aClientInfo.Name);

                    ClientInfo tempInfo;
                    NEW_CLIENT.TryRemove(aClientInfo.IP, out tempInfo);
                    AP_COUNT--;
                    catchMsg = msgTrans.MessageCombine("logout", "server", aClientInfo.Name, " 已離開聊天");
                    break;
                case "clear":
                    NEW_CLIENT.Clear();
                    AP_COUNT = 0;
                    break;
            }
            this.lblApCount.Text = "(" + AP_COUNT + ")";  //連線過來的AP數量

            List<ClientInfo> temp = NEW_CLIENT.Values.ToList<ClientInfo>();
            temp.Sort();
            this.dgvClient.DataSource = temp;

            string users = string.Empty;
            foreach (ClientInfo x in temp)
            {
                users += x.Name + "#";   //取得目前線上使用者，以#隔開
            }
            userList = msgTrans.MessageCombine("list", "server", users, "");
            SERVER.SendToAllClient(catchMsg);             //廣播給所有線上使用者，目前進入/離開聊天現況
            SERVER.SendToAllClient(userList);             //廣播給所有線上使用者，目前有哪些人在線上，以便增加使用者清單
            logger.Info(catchMsg);                        //記錄log
        }


        private void DeleteReference(string reference)
        {
            //var aUserAry = NEW_USER
        }

        //處理Client(rs,gs)傳過來的指令
        private void ClientMsgSendDeal(string msg, string aApName)
        {
            try
            {
                string[] catchMsg = msgTrans.MessageReceive(msg);
                if (catchMsg[0] == "ALL")  //全體聊天
                {
                    SERVER.SendToAllClient(msg);          //server把訊息廣播to全體
                }
                else   //與單一使用者的私密聊天
                {
                    SERVER.SendToOneClient(msg, catchMsg[0]);    //server把訊息傳給單一user
                }
                //ClientMsg(string.Format("接收來自 {0} <<", aApName), msg);
                logger.Info(msg);
                SystemMsg(DateTime.Now.ToString("HH:mm:ss"), string.Format("接收來自 {0} <<", aApName), aApName, msg);
             }
            catch (Exception ex)
            {
                SystemMsg(DateTime.Now.ToString("HH:mm:ss"), string.Format("收到 {0} 訊息格式錯誤: {2}", aApName, ex.Message), aApName, msg);
                logger.Error(DateTime.Now.ToString("HH:mm:ss"), string.Format("收到 {0} 訊息格式錯誤: {2}", aApName, ex.Message), aApName, msg);
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            if (SERVER != null)
            {
                SERVER.Disconnect();
            }
            this.btnStart.Enabled = true;
        }

        private void btnBroadCast_Click(object sender, EventArgs e)
        {
            if (SERVER != null)
            {
                string toClinetMsg = msgTrans.MessageCombine("chat", "<Server>", "ALL", this.txtSendToClient.Text);
                SERVER.SendToAllClient(toClinetMsg);
            }
            this.txtSendToClient.Text = "";
        }

        /// <summary>
        /// 傳送訊息給單一個Client
        /// </summary>
        /// <param name="msg">訊息</param>
        /// <param name="account">Client名稱</param>
        private void SendToOneClient(string msg, string account)
        {
            //SERVER.SendToOneClient(msg, account);
        }

        private void btnSendToOneClient_Click(object sender, EventArgs e)
        {
            if (lblClientName.Text != "")
            {
                string toClinetMsg = msgTrans.MessageCombine("chat", "<Server>", lblClientName.Text, this.txtToOneClient.Text);
                SERVER.SendToOneClient(toClinetMsg, this.lblClientName.Text);
                //SendToOneClient(this.txtToOneClient.Text, this.lblClientName.Text);
                //this.BeginInvoke(new ShowClientMsgDe(ShowClientMsg), new object[] { lblClientName.Text, "To " + lblClientName.Text + ": " + this.txtToOneClient.Text });
            }
            this.txtToOneClient.Text = "";
        }

        private void dgvClient_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            this.lblClientName.Text = this.dgvClient.Rows[e.RowIndex].Cells[e.ColumnIndex].Value.ToString();
        }
    }
}
