// <copyright file="Sendpulse.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace argonaut_subscription_server_proxy.SendPulse
{
    /// <summary>A sendpulse.</summary>
    internal class Sendpulse : SendpulseInterface
    {
        /// <summary>URL of the API.</summary>
        private const string _apiUrl = "https://api.sendpulse.com";

        /// <summary>URI of the API.</summary>
        private readonly Uri _apiUri = new Uri(_apiUrl);

        /// <summary>Identifier for the user.</summary>
        private string _userId;

        /// <summary>The secret.</summary>
        private string _secret;

        /// <summary>Name of the token.</summary>
        private string _tokenName;

        /// <summary>The refresh token.</summary>
        private int _refreshToken;

        /// <summary>
        /// Initializes a new instance of the <see cref="Sendpulse"/>
        /// class.
        /// </summary>
        /// <param name="userId">Identifier for the user.</param>
        /// <param name="secret">The secret.</param>
        public Sendpulse(string userId, string secret)
        {
            if (_userId == null || _secret == null)
            {
                Console.WriteLine("Empty ID or SECRET");
            }

            _userId = userId;
            _secret = secret;
            _tokenName = MD5(_userId + "::" + _secret);
            if (_tokenName != null)
            {
                if (!GetToken())
                {
                    Console.WriteLine("Could not connect to api, check your ID and SECRET");
                }
            }

            _refreshToken = 0;
        }

        /// <summary>Base 64 encode.</summary>
        /// <param name="plainText">The plain text.</param>
        /// <returns>A string.</returns>
        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        /// <summary>Md 5.</summary>
        /// <param name="input">The input.</param>
        /// <returns>A string.</returns>
        public static string MD5(string input)
        {
#pragma warning disable CA5351 // Do Not Use Broken Cryptographic Algorithms
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
#pragma warning restore CA5351 // Do Not Use Broken Cryptographic Algorithms
            {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("X2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        /// <summary>Sends a request.</summary>
        /// <param name="path">    Full pathname of the file.</param>
        /// <param name="method">  The method.</param>
        /// <param name="data">    The data.</param>
        /// <param name="useToken">(Optional) True to use token.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> SendRequest(
            string path,
            string method,
            Dictionary<string, object> data,
            bool useToken = true)
        {
            string originalPath = path;

            string value = null;
            Dictionary<string, object> response = new Dictionary<string, object>();
            try
            {
                string stringdata = string.Empty;
                if (data != null && data.Count > 0)
                {
                    stringdata = MakeRequestString(data);
                }

                method = method.ToUpperInvariant();
                if (method == "GET" && stringdata.Length > 0)
                {
                    path = path + "?" + stringdata;
                }

                HttpWebRequest webReq = (HttpWebRequest)WebRequest.Create(new Uri(_apiUri, path));
                webReq.Method = method;
                if (useToken && _tokenName != null)
                {
                    webReq.Headers.Add("Authorization", "Bearer " + _tokenName);
                }

                if (method != "GET")
                {
                    byte[] buffer = Encoding.ASCII.GetBytes(stringdata);
                    webReq.ContentType = "application/x-www-form-urlencoded";
                    webReq.ContentLength = buffer.Length;
                    Stream postData = webReq.GetRequestStream();
                    postData.Write(buffer, 0, buffer.Length);
                    postData.Close();
                }

                try
                {
                    HttpWebResponse webResp = (HttpWebResponse)webReq.GetResponse();
                    HttpStatusCode status = webResp.StatusCode;
                    response.Add("http_code", (int)status);

                    using (Stream responseStream = webResp.GetResponseStream())
                    using (StreamReader responseStreamReader = new StreamReader(responseStream))
                    {
                        value = responseStreamReader.ReadToEndAsync().Result;
                        if (value.Length > 0)
                        {
                            object jo = null;
                            try
                            {
                                jo = JsonConvert.DeserializeObject<object>(value.Trim());
                                if (jo.GetType() == typeof(JObject))
                                {
                                    jo = (JObject)jo;
                                }
                                else if (jo.GetType() == typeof(JArray))
                                {
                                    jo = (JArray)jo;
                                }
                            }
                            catch (JsonException jex)
                            {
                                Console.WriteLine(jex.Message);
                            }

                            response.Add("data", jo);
                        }
                    }
                }
                catch (WebException we)
                {
                    HttpStatusCode wRespStatusCode = ((HttpWebResponse)we.Response).StatusCode;
                    if (wRespStatusCode == HttpStatusCode.Unauthorized && _refreshToken == 0)
                    {
                        _refreshToken += 1;
                        GetToken();
                        response = SendRequest(originalPath, method, data, useToken);
                    }
                    else
                    {
                        response.Add("http_code", (int)wRespStatusCode);

                        using (Stream responseStream = ((HttpWebResponse)we.Response).GetResponseStream())
                        using (StreamReader responseStreamReader = new StreamReader(responseStream))
                        {
                            value = responseStreamReader.ReadToEndAsync().Result;
                            response.Add("data", value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return response;
        }

        /// <summary>Makes request string.</summary>
        /// <param name="data">The data.</param>
        /// <returns>A string.</returns>
        private static string MakeRequestString(Dictionary<string, object> data)
        {
            string requeststring = string.Empty;
            foreach (var item in data)
            {
                if (requeststring.Length != 0)
                {
                    requeststring = requeststring + "&";
                }

                requeststring = requeststring + HttpUtility.UrlEncode(item.Key, Encoding.UTF8) + "=" + HttpUtility.UrlEncode(item.Value.ToString(), Encoding.UTF8);
            }

            return requeststring;
        }

        /// <summary>Gets the token.</summary>
        /// <returns>True if it succeeds, false if it fails.</returns>
        private bool GetToken()
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("grant_type", "client_credentials");
            data.Add("client_id", _userId);
            data.Add("client_secret", _secret);

            Dictionary<string, object> requestResult = null;

            try
            {
                requestResult = SendRequest("oauth/access_token", "POST", data, false);
            }
            catch (IOException)
            {
            }

            if (requestResult == null)
            {
                return false;
            }

            if ((int)requestResult["http_code"] != 200)
            {
                return false;
            }

            _refreshToken = 0;
            JObject jdata = (JObject)requestResult["data"];
            if (jdata.GetType() == typeof(JObject))
            {
                _tokenName = jdata["access_token"].ToString();
            }

            return true;
        }

        /// <summary>Handles the result described by data.</summary>
        /// <param name="data">The data.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        private static Dictionary<string, object> HandleResult(Dictionary<string, object> data)
        {
            if (!data.ContainsKey("data") || data.Count == 0)
            {
                data.Add("data", null);
            }

            if ((int)data["http_code"] != 200)
            {
                data.Add("is_error", true);
            }

            return data;
        }

        /// <summary>Handles the error described by customMessage.</summary>
        /// <param name="customMessage">Message describing the custom.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        private static Dictionary<string, object> HandleError(string customMessage)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("is_error", true);
            if (customMessage != null && customMessage.Length > 0)
            {
                data.Add("message", customMessage);
            }

            return data;
        }

        /// <summary>Get list of address books.</summary>
        /// <param name="limit"> Maximum number of results returned by this function.</param>
        /// <param name="offset"> Offset of first result returned (for paging).</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> ListAddressBooks(int limit, int offset)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            if (limit > 0)
            {
                data.Add("limit", limit);
            }

            if (offset > 0)
            {
                data.Add("offset", offset);
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("addressbooks", "GET", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get book info.</summary>
        /// <param name="id">.</param>
        /// <returns>The book information.</returns>
        public Dictionary<string, object> GetBookInfo(int id)
        {
            if (id <= 0)
            {
                return HandleError("Empty book id");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("addressbooks/" + id, "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get list pf emails from book.</summary>
        /// <param name="id">.</param>
        /// <returns>The emails from book.</returns>
        public Dictionary<string, object> GetEmailsFromBook(int id)
        {
            if (id <= 0)
            {
                return HandleError("Empty book id");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("addressbooks/" + id + "/emails", "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Remove address book.</summary>
        /// <param name="id">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> RemoveAddressBook(int id)
        {
            if (id <= 0)
            {
                return HandleError("Empty book id");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("addressbooks/" + id, "DELETE", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Edit address book name.</summary>
        /// <param name="id">     String book id.</param>
        /// <param name="newname">String book new name.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> EditAddressBook(int id, string newname)
        {
            if (id <= 0 || newname.Length == 0)
            {
                return HandleError("Empty new name or book id");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("name", newname);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("addressbooks/" + id, "PUT", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Create new address book.</summary>
        /// <param name="bookName">.</param>
        /// <returns>The new address book.</returns>
        public Dictionary<string, object> CreateAddressBook(string bookName)
        {
            if (bookName.Length == 0)
            {
                return HandleError("Empty book name");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("bookName", bookName);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("addressbooks", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Add new emails to book.</summary>
        /// <param name="bookId">int book id.</param>
        /// <param name="emails">A serialized array of emails.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> AddEmails(int bookId, string emails)
        {
            if (bookId <= 0 || emails.Length == 0)
            {
                return HandleError("Empty book id or emails");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("emails", emails);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("addressbooks/" + bookId + "/emails", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Remove emails from book.</summary>
        /// <param name="bookId">int book id.</param>
        /// <param name="emails">String A serialized array of emails.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> RemoveEmails(int bookId, string emails)
        {
            if (bookId <= 0 || emails.Length == 0)
            {
                return HandleError("Empty book id or emails");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("emails", emails);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("addressbooks/" + bookId + "/emails", "DELETE", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get information about email from book.</summary>
        /// <param name="bookId">Address book id.</param>
        /// <param name="email"> Email address.</param>
        /// <returns>The email information.</returns>
        public Dictionary<string, object> GetEmailInfo(int bookId, string email)
        {
            if (bookId <= 0 || email.Length == 0)
            {
                return HandleError("Empty book id or email");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("addressbooks/" + bookId + "/emails/" + email, "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Calculate cost of the campaign based on address book.</summary>
        /// <param name="bookId">Address book id.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> CampaignCost(int bookId)
        {
            if (bookId <= 0)
            {
                return HandleError("Empty book id");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("addressbooks/" + bookId + "/cost", "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get list of campaigns.</summary>
        /// <param name="limit"> Maximum number of results returned by this function.</param>
        /// <param name="offset"> Offset of first result returned (for paging).</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> ListCampaigns(int limit, int offset)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            if (limit > 0)
            {
                data.Add("limit", limit);
            }

            if (offset > 0)
            {
                data.Add("offset", offset);
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("campaigns", "GET", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get information about campaign.</summary>
        /// <param name="id">.</param>
        /// <returns>The campaign information.</returns>
        public Dictionary<string, object> GetCampaignInfo(int id)
        {
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("campaigns/" + id, "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get campaign statistic by countries.</summary>
        /// <param name="id">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> CampaignStatByCountries(int id)
        {
            if (id <= 0)
            {
                return HandleError("Empty campaign id");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("campaigns/" + id + "/countries", "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get campaign statistic by referrals.</summary>
        /// <param name="id">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> CampaignStatByReferrals(int id)
        {
            if (id <= 0)
            {
                return HandleError("Empty campaign id");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("campaigns/" + id + "/referrals", "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Create new campaign.</summary>
        /// <param name="senderName"> Sender name.</param>
        /// <param name="senderEmail">Sender email address.</param>
        /// <param name="subject">    Subject of message.</param>
        /// <param name="body">       Body of message.</param>
        /// <param name="bookId">     Address book id.</param>
        /// <param name="name">       Name.</param>
        /// <param name="send_date">  (Optional) The send date.</param>
        /// <param name="attachments">(Optional) Attachments to include.</param>
        /// <returns>The new campaign.</returns>
        public Dictionary<string, object> CreateCampaign(
            string senderName,
            string senderEmail,
            string subject,
            string body,
            int bookId,
            string name,
            string send_date = "",
            string attachments = "")
        {
            if (senderName.Length == 0 || senderEmail.Length == 0 || subject.Length == 0 || body.Length == 0 || bookId <= 0)
            {
                return HandleError("Not all data.");
            }

            string encodedBody = Base64Encode(body);
            Dictionary<string, object> data = new Dictionary<string, object>();
            if (attachments.Length > 0)
            {
                data.Add("attachments", attachments);
            }

            if (send_date.Length > 0)
            {
                data.Add("send_date", send_date);
            }

            data.Add("sender_name", senderName);
            data.Add("sender_email", senderEmail);
            data.Add("subject", subject);
            if (encodedBody.Length > 0)
            {
                data.Add("body", encodedBody.ToString());
            }

            data.Add("list_id", bookId);
            if (name.Length > 0)
            {
                data.Add("name", name);
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("campaigns", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Cancel campaign.</summary>
        /// <param name="id">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> CancelCampaign(int id)
        {
            if (id <= 0)
            {
                return HandleError("Empty campaign id");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("campaigns/" + id, "DELETE", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get list of allowed senders.</summary>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> ListSenders()
        {
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("senders", "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Add new sender.</summary>
        /// <param name="senderName"> Sender name.</param>
        /// <param name="senderEmail">Sender email address.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> AddSender(string senderName, string senderEmail)
        {
            if (senderName.Length == 0 || senderEmail.Length == 0)
            {
                return HandleError("Empty sender name or email");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("name", senderName);
            data.Add("email", senderEmail);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("senders", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Remove sender.</summary>
        /// <param name="email">The email address.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> removeSender(string email)
        {
            if (email.Length == 0)
            {
                return HandleError("Empty email");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("email", email);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("senders", "DELETE", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Activate sender using code from mail.</summary>
        /// <param name="email">The email address.</param>
        /// <param name="code"> The code.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> activateSender(string email, string code)
        {
            if (email.Length == 0 || code.Length == 0)
            {
                return HandleError("Empty email or activation code");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("code", code);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("senders/" + email + "/code", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Send mail with activation code on sender email.</summary>
        /// <param name="email">.</param>
        /// <returns>The sender activation mail.</returns>
        public Dictionary<string, object> getSenderActivationMail(string email)
        {
            if (email.Length == 0)
            {
                return HandleError("Empty email");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("senders/" + email + "/code", "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get global information about email.</summary>
        /// <param name="email">.</param>
        /// <returns>The email global information.</returns>
        public Dictionary<string, object> getEmailGlobalInfo(string email)
        {
            if (email.Length == 0)
            {
                return HandleError("Empty email");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("emails/" + email, "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Remove email address from all books.</summary>
        /// <param name="email">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> removeEmailFromAllBooks(string email)
        {
            if (email.Length == 0)
            {
                return HandleError("Empty email");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("emails/" + email, "DELETE", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get statistic for email by all campaigns.</summary>
        /// <param name="email">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> emailStatByCampaigns(string email)
        {
            if (email.Length == 0)
            {
                return HandleError("Empty email");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("emails/" + email + "/campaigns", "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Show emails from blacklist.</summary>
        /// <returns>The black list.</returns>
        public Dictionary<string, object> getBlackList()
        {
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("blacklist", "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Add email address to blacklist.</summary>
        /// <param name="emails">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> addToBlackList(string emails)
        {
            if (emails.Length == 0)
            {
                return HandleError("Empty emails");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            string encodedemails = Base64Encode(emails);
            data.Add("emails", encodedemails);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("blacklist", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Remove email address from blacklist.</summary>
        /// <param name="emails">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> removeFromBlackList(string emails)
        {
            if (emails.Length == 0)
            {
                return HandleError("Empty emails");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            string encodedemails = Base64Encode(emails);
            data.Add("emails", encodedemails);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("blacklist", "DELETE", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Return user balance.</summary>
        /// <param name="currency">.</param>
        /// <returns>The balance.</returns>
        public Dictionary<string, object> getBalance(string currency)
        {
            string url = "balance";
            if (currency.Length > 0)
            {
                currency = currency.ToUpperInvariant();
                url = url + "/" + currency;
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest(url, "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Send mail using SMTP.</summary>
        /// <param name="emaildata">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> smtpSendMail(Dictionary<string, object> emaildata)
        {
            if (emaildata.Count == 0)
            {
                return HandleError("Empty email data");
            }

            string html = emaildata["html"].ToString();
            emaildata.Remove("html");
            emaildata.Add("html", Base64Encode(html));
            Dictionary<string, object> data = new Dictionary<string, object>();
            string serialized = JsonConvert.SerializeObject(emaildata);
            data.Add("email", serialized);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("smtp/emails", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get list of emails that was sent by SMTP.</summary>
        /// <param name="limit">    The limit.</param>
        /// <param name="offset">   The offset.</param>
        /// <param name="fromDate"> from date.</param>
        /// <param name="toDate">   to date.</param>
        /// <param name="sender">   The sender.</param>
        /// <param name="recipient">The recipient.</param>
        /// <returns>A Dictionary of email info.</returns>
        public Dictionary<string, object> smtpListEmails(
            int limit,
            int offset,
            string fromDate,
            string toDate,
            string sender,
            string recipient)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("limit", limit);
            data.Add("offset", offset);
            if (fromDate.Length > 0)
            {
                data.Add("fromDate", fromDate);
            }

            if (toDate.Length > 0)
            {
                data.Add("toDate", toDate);
            }

            if (sender.Length > 0)
            {
                data.Add("sender", sender);
            }

            if (recipient.Length > 0)
            {
                data.Add("recipient", recipient);
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("smtp/emails", "GET", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get information about email by his id.</summary>
        /// <param name="id">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> smtpGetEmailInfoById(string id)
        {
            if (id.Length == 0)
            {
                return HandleError("Empty id");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("smtp/emails/" + id, "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Unsubscribe emails using SMTP.</summary>
        /// <param name="emails">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> smtpUnsubscribeEmails(string emails)
        {
            if (emails.Length == 0)
            {
                return HandleError("Empty emails");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("emails", emails);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("/smtp/unsubscribe", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Remove emails from unsubscribe list using SMTP.</summary>
        /// <param name="emails">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> smtpRemoveFromUnsubscribe(string emails)
        {
            if (emails.Length == 0)
            {
                return HandleError("Empty emails");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("emails", emails);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("/smtp/unsubscribe", "DELETE", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get list of allowed IPs using SMTP.</summary>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> smtpListIP()
        {
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("smtp/ips", "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get list of allowed domains using SMTP.</summary>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> smtpListAllowedDomains()
        {
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("smtp/domains", "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Add domain using SMTP.</summary>
        /// <param name="email">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> smtpAddDomain(string email)
        {
            if (email.Length == 0)
            {
                return HandleError("Empty email");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("email", email);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("smtp/domains", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Send confirm mail to verify new domain.</summary>
        /// <param name="email">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> smtpVerifyDomain(string email)
        {
            if (email.Length == 0)
            {
                return HandleError("Empty email");
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("smtp/domains/" + email, "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get list of push campaigns.</summary>
        /// <param name="limit"> Maximum number of results returned by this function.</param>
        /// <param name="offset"> Offset of first result returned (for paging).</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> pushListCampaigns(int limit, int offset)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            if (limit > 0)
            {
                data.Add("limit", limit);
            }

            if (offset > 0)
            {
                data.Add("offset", offset);
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("push/tasks", "GET", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get push campaigns info.</summary>
        /// <param name="id">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> pushCampaignInfo(int id)
        {
            if (id > 0)
            {
                Dictionary<string, object> result = null;
                try
                {
                    result = SendRequest("push/tasks/" + id, "GET", null);
                }
                catch (IOException)
                {
                }

                return HandleResult(result);
            }
            else
            {
                return HandleError("No such push campaign");
            }
        }

        /// <summary>Get amount of websites.</summary>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> pushCountWebsites()
        {
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("push/websites/total", "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get list of websites.</summary>
        /// <param name="limit"> Maximum number of results returned by this function.</param>
        /// <param name="offset"> Offset of first result returned (for paging).</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> pushListWebsites(int limit, int offset)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            if (limit > 0)
            {
                data.Add("limit", limit);
            }

            if (offset > 0)
            {
                data.Add("offset", offset);
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("push/websites", "GET", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get list of all variables for website.</summary>
        /// <param name="id">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> pushListWebsiteVariables(int id)
        {
            Dictionary<string, object> result = null;
            string url = string.Empty;
            if (id > 0)
            {
                url = "push/websites/" + id + "/variables";
                try
                {
                    result = SendRequest(url, "GET", null);
                }
                catch (IOException)
                {
                }
            }

            return HandleResult(result);
        }

        /// <summary>Get list of subscriptions for the website.</summary>
        /// <param name="id">    .</param>
        /// <param name="limit"> Maximum number of results returned by this function.</param>
        /// <param name="offset"> Offset of first result returned (for paging).</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> pushListWebsiteSubscriptions(int id, int limit, int offset)
        {
            Dictionary<string, object> result = null;
            string url = string.Empty;
            if (id > 0)
            {
                Dictionary<string, object> data = new Dictionary<string, object>();
                if (limit > 0)
                {
                    data.Add("limit", limit);
                }

                if (offset > 0)
                {
                    data.Add("offset", offset);
                }

                url = "push/websites/" + id + "/subscriptions";
                try
                {
                    result = SendRequest(url, "GET", data);
                }
                catch (IOException)
                {
                }

                return HandleResult(result);
            }
            else
            {
                return HandleError("Empty ID");
            }
        }

        /// <summary>Get amount of subscriptions for the site.</summary>
        /// <param name="id">.</param>
        /// <returns>A Dictionary&lt;string,object&gt;.</returns>
        public Dictionary<string, object> pushCountWebsiteSubscriptions(int id)
        {
            Dictionary<string, object> result = null;
            string url = string.Empty;
            if (id > 0)
            {
                url = "push/websites/" + id + "/subscriptions/total";
                try
                {
                    result = SendRequest(url, "GET", null);
                }
                catch (IOException)
                {
                }

                return HandleResult(result);
            }
            else
            {
                return HandleError("Empty ID");
            }
        }

        /// <summary>Set state for subscription.</summary>
        /// <param name="id">   The identifier.</param>
        /// <param name="state">The state.</param>
        /// <returns>A Dictionary.</returns>
        public Dictionary<string, object> pushSetSubscriptionState(int id, int state)
        {
            if (id > 0)
            {
                Dictionary<string, object> data = new Dictionary<string, object>();
                data.Add("id", id);
                data.Add("state", state);
                Dictionary<string, object> result = null;
                try
                {
                    result = SendRequest("push/subscriptions/state", "POST", data);
                }
                catch (IOException)
                {
                }

                return HandleResult(result);
            }
            else
            {
                return HandleError("Empty ID");
            }
        }

        /// <summary>Create new push campaign.</summary>
        /// <param name="taskinfo">        The taskinfo.</param>
        /// <param name="additionalParams">Options for controlling the additional.</param>
        /// <returns>The new push task.</returns>
        public Dictionary<string, object> createPushTask(Dictionary<string, object> taskinfo, Dictionary<string, object> additionalParams)
        {
            Dictionary<string, object> data = taskinfo;
            if (!data.ContainsKey("ttl"))
            {
                data.Add("ttl", 0);
            }

            if (!data.ContainsKey("title") || !data.ContainsKey("website_id") || !data.ContainsKey("body"))
            {
                return HandleError("Not all data");
            }

            if (additionalParams != null && additionalParams.Count > 0)
            {
                foreach (var item in additionalParams)
                {
                    data.Add(item.Key, item.Value);
                }
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("push/tasks", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Add phones to address book.</summary>
        /// <param name="bookId">Book identifier.</param>
        /// <param name="phones">Phones.</param>
        /// <returns>The phones.</returns>
        public Dictionary<string, object> addPhones(int bookId, string phones)
        {
            if (bookId <= 0 || phones.Length == 0)
            {
                return HandleError("Empty book id or phones");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("phones", phones);
            data.Add("addressBookId", bookId);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("/sms/numbers", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Remove phones from address book.</summary>
        /// <param name="bookId">Book identifier.</param>
        /// <param name="phones">Phones.</param>
        /// <returns>The phones.</returns>
        public Dictionary<string, object> removePhones(int bookId, string phones)
        {
            if (bookId <= 0 || phones.Length == 0)
            {
                return HandleError("Empty book id or phones");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("phones", phones);
            data.Add("addressBookId", bookId);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("/sms/numbers", "DELETE", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Update phones.</summary>
        /// <param name="bookId">   Book identifier.</param>
        /// <param name="phones">   Phones.</param>
        /// <param name="variables">Variables.</param>
        /// <returns>The phones.</returns>
        public Dictionary<string, object> updatePhones(int bookId, string phones, string variables)
        {
            if (bookId <= 0 || phones.Length == 0)
            {
                return HandleError("Empty book id or phones");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("phones", phones);
            data.Add("variables", variables);
            data.Add("addressBookId", bookId);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("/sms/numbers", "PUT", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get the phone number info.</summary>
        /// <param name="bookId">     Book identifier.</param>
        /// <param name="phoneNumber">Phone number.</param>
        /// <returns>The phone info.</returns>
        public Dictionary<string, object> getPhoneInfo(int bookId, string phoneNumber)
        {
            Dictionary<string, object> result = null;
            string url = string.Empty;
            if (bookId > 0)
            {
                url = "/sms/numbers/info/" + bookId + "/" + phoneNumber;
                try
                {
                    result = SendRequest(url, "GET", null);
                }
                catch (IOException)
                {
                }

                return HandleResult(result);
            }
            else
            {
                return HandleError("Empty ID");
            }
        }

        /// <summary>Add phones to black list.</summary>
        /// <param name="phones">     Phones.</param>
        /// <param name="description">Description.</param>
        /// <returns>The phone to black list.</returns>
        public Dictionary<string, object> addPhonesToBlackList(string phones, string description)
        {
            if (phones.Length == 0)
            {
                return HandleError("Empty phones");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("phones", phones);
            if (description != null)
            {
                data.Add("description", description);
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("/sms/black_list", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Remove phones from black list.</summary>
        /// <param name="phones">Phones.</param>
        /// <returns>The phones from black list.</returns>
        public Dictionary<string, object> removePhonesFromBlackList(string phones)
        {
            if (phones.Length == 0)
            {
                return HandleError("Empty phones");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("phones", phones);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("/sms/black_list", "DELETE", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get black list of phone numbers.</summary>
        /// <returns>The black list phones.</returns>
        public Dictionary<string, object> getBlackListPhones()
        {
            Dictionary<string, object> result = null;
            string url = "/sms/black_list";
            try
            {
                result = SendRequest(url, "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Retrieving information of telephone numbers in the blacklist.</summary>
        /// <param name="phones">The phones.</param>
        /// <returns>The phones info in black list.</returns>
        public Dictionary<string, object> getPhonesInfoInBlackList(string phones)
        {
            Dictionary<string, object> result = null;
            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("phones", phones);
            string url = "/sms/black_list/by_numbers";
            try
            {
                result = SendRequest(url, "GET", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Send the sms campaign.</summary>
        /// <param name="bookId">       Book identifier.</param>
        /// <param name="body">         Body.</param>
        /// <param name="transliterate">(Optional) Transliterate.</param>
        /// <param name="sender">       (Optional) Sender.</param>
        /// <param name="date">         (Optional) Date.</param>
        /// <returns>The sms campaign.</returns>
        public Dictionary<string, object> sendSmsCampaign(int bookId, string body, int transliterate = 1, string sender = "", string date = "")
        {
            if (body.Length == 0)
            {
                return HandleError("Empty Body");
            }

            if (bookId <= 0)
            {
                return HandleError("Empty address book Id");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("addressBookId", bookId);
            data.Add("body", body);
            if (sender != null)
            {
                data.Add("sender", sender);
            }

            data.Add("transliterate", transliterate);
            if (date != null)
            {
                data.Add("date", date);
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("/sms/campaigns", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Send sms campaign by phones list.</summary>
        /// <param name="phones">       Phones.</param>
        /// <param name="body">         Body.</param>
        /// <param name="transliterate">(Optional) Transliterate.</param>
        /// <param name="sender">       (Optional) Sender.</param>
        /// <param name="date">         (Optional) Date.</param>
        /// <returns>The sms campaign by phones.</returns>
        public Dictionary<string, object> sendSmsCampaignByPhones(string phones, string body, int transliterate = 1, string sender = "", string date = "")
        {
            if (body.Length == 0)
            {
                return HandleError("Empty Body");
            }

            if (phones.Length == 0)
            {
                return HandleError("Empty phones");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("phones", phones);
            data.Add("body", body);
            if (sender != null)
            {
                data.Add("sender", sender);
            }

            data.Add("transliterate", transliterate);
            if (date != null)
            {
                data.Add("date", date);
            }

            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("/sms/send", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get sms campaigns list.</summary>
        /// <param name="dateFrom">Date from.</param>
        /// <param name="dateTo">  Date to.</param>
        /// <returns>The sms campaigns list.</returns>
        public Dictionary<string, object> getSmsCampaignsList(string dateFrom, string dateTo)
        {
            Dictionary<string, object> result = null;
            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("dateFrom", dateFrom);
            data.Add("dateTo", dateTo);
            string url = "/sms/campaigns/list";
            try
            {
                result = SendRequest(url, "GET", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get sms campaign info.</summary>
        /// <param name="id">Identifier.</param>
        /// <returns>The sms campaign info.</returns>
        public Dictionary<string, object> getSmsCampaignInfo(int id)
        {
            Dictionary<string, object> result = null;
            string url = string.Empty;
            if (id > 0)
            {
                url = "/sms/campaigns/info/" + id;
                try
                {
                    result = SendRequest(url, "GET", null);
                }
                catch (IOException)
                {
                }

                return HandleResult(result);
            }
            else
            {
                return HandleError("Empty ID");
            }
        }

        /// <summary>Cancel sms campaign.</summary>
        /// <param name="id">Identifier.</param>
        /// <returns>The sms campaign.</returns>
        public Dictionary<string, object> cancelSmsCampaign(int id)
        {
            Dictionary<string, object> result = null;
            string url = string.Empty;
            if (id > 0)
            {
                url = "/sms/campaigns/cancel/" + id;
                try
                {
                    result = SendRequest(url, "GET", null);
                }
                catch (IOException)
                {
                }

                return HandleResult(result);
            }
            else
            {
                return HandleError("Empty ID");
            }
        }

        /// <summary>Get sms campaign cost.</summary>
        /// <param name="body">         Body.</param>
        /// <param name="sender">       Sender.</param>
        /// <param name="addressBookId">(Optional) Address book identifier.</param>
        /// <param name="phones">       (Optional) Phones.</param>
        /// <returns>The sms campaign cost.</returns>
        public Dictionary<string, object> getSmsCampaignCost(string body, string sender, int addressBookId = 0, string phones = "")
        {
            if (body.Length == 0)
            {
                return HandleError("Empty Body");
            }

            Dictionary<string, object> result = null;
            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("body", body);
            data.Add("sender", sender);
            if (addressBookId <= 0 && phones == null)
            {
                return HandleError("Empty recipients list");
            }
            else if (phones.Length > 0)
            {
                data.Add("phones", phones);
            }
            else
            {
                data.Add("addressBookId", addressBookId);
            }

            string url = "/sms/campaigns/cost";
            try
            {
                result = SendRequest(url, "GET", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Delete sms campaign.</summary>
        /// <param name="id">Identifier.</param>
        /// <returns>The sms campaign.</returns>
        public Dictionary<string, object> deleteSmsCampaign(int id)
        {
            Dictionary<string, object> result = null;
            string url = string.Empty;
            if (id > 0)
            {
                Dictionary<string, object> data = new Dictionary<string, object>();
                data.Add("id", id);
                url = "/sms/campaigns";
                try
                {
                    result = SendRequest(url, "DELETE", data);
                }
                catch (IOException)
                {
                }

                return HandleResult(result);
            }
            else
            {
                return HandleError("Empty ID");
            }
        }

        /// <summary>Add phones to addreess book.</summary>
        /// <param name="addressBookId">Address book identifier.</param>
        /// <param name="phones">       Phones.</param>
        /// <returns>The phones to addreess book.</returns>
        public Dictionary<string, object> addPhonesToAddreessBook(int addressBookId, string phones)
        {
            if (addressBookId <= 0)
            {
                return HandleError("Empty address book id");
            }

            if (phones.Length == 0)
            {
                return HandleError("Empty phones");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("phones", phones);
            data.Add("addressBookId", addressBookId);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("/sms/numbers/variables", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Send viber campaign.</summary>
        /// <param name="recipients">     Recipients.</param>
        /// <param name="addressBookId">  Address book identifier.</param>
        /// <param name="message">        Message.</param>
        /// <param name="senderId">       Sender identifier.</param>
        /// <param name="additional">     Additional identifier.</param>
        /// <param name="messageLiveTime">(Optional) Message live time.</param>
        /// <param name="sendDate">       (Optional) Send date.</param>
        /// <returns>The viber campaign.</returns>
        public Dictionary<string, object> sendViberCampaign(
            string recipients,
            int addressBookId,
            string message,
            int senderId,
            string additional,
            int messageLiveTime = 60,
            string sendDate = "now")
        {
            if (addressBookId <= 0 && recipients.Length == 0)
            {
                return HandleError("Empty recipients list");
            }

            if (message.Length == 0)
            {
                return HandleError("Empty message");
            }

            if (senderId <= 0)
            {
                return HandleError("Empty sender");
            }

            Dictionary<string, object> data = new Dictionary<string, object>();
            if (recipients.Length > 0)
            {
                data.Add("recipients", recipients);
            }
            else if (addressBookId > 0)
            {
                data.Add("address_book", addressBookId);
            }

            data.Add("message", message);
            data.Add("sender_id", senderId);
            data.Add("send_date", sendDate);
            data.Add("additional", additional);
            data.Add("message_live_time", messageLiveTime);
            Dictionary<string, object> result = null;
            try
            {
                result = SendRequest("/viber", "POST", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get viber senders list.</summary>
        /// <returns>The viber senders.</returns>
        public Dictionary<string, object> getViberSenders()
        {
            Dictionary<string, object> result = null;
            string url = "/viber/senders";
            try
            {
                result = SendRequest(url, "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get viber tasks list.</summary>
        /// <param name="limit"> (Optional) Limit.</param>
        /// <param name="offset">(Optional) Offset.</param>
        /// <returns>The viber tasks list.</returns>
        public Dictionary<string, object> getViberTasksList(int limit = 100, int offset = 0)
        {
            Dictionary<string, object> result = null;
            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("limit", limit);
            data.Add("offset", offset);
            string url = "/viber/task";
            try
            {
                result = SendRequest(url, "GET", data);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get viber campaign statistic.</summary>
        /// <param name="id">Identifier.</param>
        /// <returns>The viber campaign stat.</returns>
        public Dictionary<string, object> getViberCampaignStat(int id)
        {
            Dictionary<string, object> result = null;
            if (id <= 0)
            {
                return HandleError("Empty id");
            }

            string url = "/viber/task/" + id;
            try
            {
                result = SendRequest(url, "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get the viber sender info.</summary>
        /// <param name="id">Identifier.</param>
        /// <returns>The viber sender.</returns>
        public Dictionary<string, object> getViberSender(int id)
        {
            Dictionary<string, object> result = null;
            if (id <= 0)
            {
                return HandleError("Empty id");
            }

            string url = "/viber/senders/" + id;
            try
            {
                result = SendRequest(url, "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }

        /// <summary>Get viber task recipients.</summary>
        /// <param name="id">Identifier.</param>
        /// <returns>The viber task recipients.</returns>
        public Dictionary<string, object> getViberTaskRecipients(int id)
        {
            Dictionary<string, object> result = null;
            if (id <= 0)
            {
                return HandleError("Empty id");
            }

            string url = "/viber/task/" + id + "/recipients";
            try
            {
                result = SendRequest(url, "GET", null);
            }
            catch (IOException)
            {
            }

            return HandleResult(result);
        }
    }
}
