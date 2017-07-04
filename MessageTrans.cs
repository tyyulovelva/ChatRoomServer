using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatroomMainForm
{
    /// <summary>
    /// 使用 JSON  來實現 server 與 client、client 與 client 間的訊息接收與傳送
    /// 將傳送與接收的訊息類型、時間、來自誰傳的、傳給誰、訊息內容，組合成 JSON，並回傳 JSON 格式的字串
    /// </summary>
    public class MessageTrans
    {
        private JObject jMessage;                          //負責訊息組合的 JObject
        private string OnlineUsers = string.Empty;         //記錄線上使用者名單
        private string returnMsg = string.Empty;           //回傳的訊息

        public MessageTrans() { }

        /// <summary>
        /// 將訊息組合成json格式並回傳
        /// </summary>
        /// <param name="type">訊息類型</param>
        /// <param name="from">來自誰傳的</param>
        /// <param name="to">傳給誰</param>
        /// <param name="msg">訊息內容</param>
        /// <returns></returns>
        public string MessageCombine(string type, string from, string to, string msg)
        {
            jMessage = new JObject();
            jMessage.Add(new JProperty("type", type));
            jMessage.Add(new JProperty("time", DateTime.Now.ToString("HH:mm:ss")));
            jMessage.Add(new JProperty("from", from));
            jMessage.Add(new JProperty("to", to));
            jMessage.Add(new JProperty("message", msg));

            returnMsg = JsonConvert.SerializeObject(jMessage, Formatting.None);
            return returnMsg;
        }

        /// <summary>
        /// 接收 json格式字串，拆解後回傳訊息
        /// </summary>
        /// <param name="msg"></param>
        public string[] MessageReceive(string msg)
        {
            jMessage = JObject.Parse(msg);                       //拆解傳過來的 json 訊息
            string type = jMessage["type"].ToString();           //訊息類型
            string time = jMessage["time"].ToString();           //時間戳記
            string from = jMessage["from"].ToString();           //來自誰傳的
            string user = jMessage["to"].ToString();             //傳給誰
            string message = jMessage["message"].ToString();     //傳遞的訊息
            string[] returnMsgAry = new string[2] {"",""};

            if (type == "connect")   //成功連線
            {
                returnMsgAry[1] = time + " " + message + "\r\n";
            }
            else if (type == "login")   //client登入
            {
                returnMsgAry[1] = time + " " + user + message + "\r\n";
            }
            else if (type == "logout")   //client登出
            {
                returnMsgAry[1] = time + " " + user + message + "\r\n";
            }
            else if (type == "chat")   // client聊天
            {
                returnMsgAry[0] = user;
                if (user == "ALL")
                {
                    returnMsgAry[1] = time + " " + from + " 對大家說: " + message + "\r\n";
                }
                else
                {
                    returnMsgAry[1] = time + " " + from + " 悄悄對你說: " + message + "\r\n";
                }
            }
            return returnMsgAry;
        }
    }
}
