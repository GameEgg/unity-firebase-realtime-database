using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace FirebaseREST
{
    public partial class DatabaseReference : Query
    {
        string reference;
        string orderBy;
        string endAt;
        string startAt;
        string equalTo;
        string limitToFirst;
        string limitToLast;

        EventHandler<ValueChangedEventArgs> _ValueChanged;
        FirebaseServerEventResponse eventResponse;

        UnityWebRequest webReq;
#if UNITY_WEBGL
        FirebaseDatabase.FirebaseEventSourceWebGL esGL;
#endif
        object CacheData;

        int childMovedRefCount = 0, childChangedRefCount = 0, childAddedRefCount = 0, childRemovedRefCount = 0, valueChangedRefCount = 0;

        public override event EventHandler<FirebaseDatabaseErrorEventArgs> DatabaseError;
        public override event EventHandler HeartBeat;
        public event EventHandler Disposed;

        public override event EventHandler<ValueChangedEventArgs> ValueChanged
        {
            add
            {
                valueChangedRefCount++;
                BeginListeningServerEvents();
                _ValueChanged += value;
            }
            remove
            {
                valueChangedRefCount--;
                DisposedUnityWebRequestIfNoReferences();
                _ValueChanged -= value;
            }
        }

        async void BeginListeningServerEvents()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (esGL != null) return;
            string url = this.ReferenceUrl;
            if (FirebaseAuth.Instance.IsSignedIn)
            {
                // 비동기 버전으로 await 사용 불가하므로 기존 콜백 호출 유지
                FirebaseAuth.Instance.GetAccessToken((accessToken) =>
                {
                    url = url + "?auth=" + accessToken;
                    esGL = new FirebaseDatabase.FirebaseEventSourceWebGL(url, true, null,
                        OnEventSourceMessageReceived, OnEventSourceError);
                });
            }
            else
            {
                esGL = new FirebaseDatabase.FirebaseEventSourceWebGL(url, false, null,
                    OnEventSourceMessageReceived, OnEventSourceError);
            }
#else
            if (webReq != null) return;
            string url = this.ReferenceUrl;
            Action sendRequest = () =>
            {
                webReq = new UnityWebRequest(url);
                webReq.SetRequestHeader("Accept", "text/event-stream");
                webReq.SetRequestHeader("Cache-Control", "no-cache");
                FirebaseServerEventsDownloadHandler downloadHandler = new FirebaseServerEventsDownloadHandler();
                downloadHandler.DataReceived += OnDataReceived;
                webReq.downloadHandler = downloadHandler;
                webReq.disposeDownloadHandlerOnDispose = true;
                UnityWebRequestAsyncOperation webReqAO = webReq.SendWebRequest();
                webReqAO.completed += ((ao) => OnStopListening(webReqAO));
            };

            if (FirebaseAuth.Instance.IsSignedIn)
            {
                var accessToken = await FirebaseAuth.Instance.GetAccessToken();
                url = url + "?auth=" + accessToken;
                sendRequest();
            }
            else
                sendRequest();
#endif
        }

#if UNITY_WEBGL
        private void OnEventSourceError(FirebaseEventSourceErrorArgs obj)
        {
            FirebaseDatabaseErrorCode code;
            try
            {
                code = (FirebaseDatabaseErrorCode)Enum.Parse(typeof(FirebaseDatabaseErrorCode), obj.Error);
                DatabaseError?.Invoke(this, new FirebaseDatabaseErrorEventArgs(new DatabaseError(code)));
            }
            catch
            {
                DatabaseError?.Invoke(this, new FirebaseDatabaseErrorEventArgs(new DatabaseError(FirebaseDatabaseErrorCode.NetworkError)));
            }
        }

        private void OnEventSourceMessageReceived(FirebaseEventSourceMessageArgs obj)
        {
            OnDataReceived("event: " + obj.EventBuffer + "\ndata: " + obj.DataBuffer);
        }
#endif

        private void OnStopListening(UnityWebRequestAsyncOperation obj)
        {
            if (obj.webRequest.result == UnityWebRequest.Result.ConnectionError)
            {
                Debug.LogError("Network error");
                DatabaseError?.Invoke(this, new FirebaseDatabaseErrorEventArgs(new DatabaseError(FirebaseDatabaseErrorCode.NetworkError)));
            }
            else
            {
                Debug.LogWarning(obj.webRequest.responseCode + "-" + obj.webRequest.downloadHandler.text);
                FirebaseDatabaseErrorCode code;
                try
                {
                    code = (FirebaseDatabaseErrorCode)Enum.Parse(typeof(FirebaseDatabaseErrorCode), obj.webRequest.downloadHandler.text);
                    DatabaseError?.Invoke(this, new FirebaseDatabaseErrorEventArgs(new DatabaseError(code)));
                }
                catch
                {
                    DatabaseError?.Invoke(this, new FirebaseDatabaseErrorEventArgs(new DatabaseError(FirebaseDatabaseErrorCode.NetworkError)));
                }
            }
            obj.webRequest.Dispose();
        }

        private void OnDataReceived(string data)
        {
            string[] lines = data.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (eventResponse == null || (string.IsNullOrEmpty(eventResponse.eventType) && eventResponse.data != null))
                    eventResponse = new FirebaseServerEventResponse();

                if (string.IsNullOrEmpty(lines[i]) || lines[i].Trim().Length == 0) continue;
                string[] arr = lines[i].Split(new string[] { ": " }, 2, StringSplitOptions.None);
                if (arr.Length > 1)
                {
                    switch (arr[0])
                    {
                        case "event":
                            eventResponse.eventType = arr[1];
                            break;
                        case "data":
                            switch (eventResponse.eventType)
                            {
                                case "put":
                                    {
                                        var dataObject = JsonConvert.DeserializeObject<JObject>(arr[1]);
                                        eventResponse.data = new FirebaseServerEventData(dataObject["path"].ToString(), dataObject["data"]);
                                        ProcessEventPutData(eventResponse.data);
                                        HeartBeat?.Invoke(this, EventArgs.Empty);
                                    }
                                    break;
                                case "patch":
                                    {
                                        var dataObject = JsonConvert.DeserializeObject<JObject>(arr[1]);
                                        eventResponse.data = new FirebaseServerEventData(dataObject["path"].ToString(), dataObject["data"]);
                                        ProcessEventPatchData(eventResponse.data);
                                        HeartBeat?.Invoke(this, EventArgs.Empty);
                                    }
                                    break;
                                case "keep_alive":
                                    HeartBeat?.Invoke(this, EventArgs.Empty);
                                    break;
                                case "auth_revoked":
                                    HeartBeat?.Invoke(this, EventArgs.Empty);
                                    break;
                            }
                            break;
                    }
                }
            }
        }

        void ProcessEventPutData(FirebaseServerEventData eventData)
        {
            string[] paths = eventData.path.Trim('/').Split('/');
            
            if (CacheData == null || !(CacheData is JObject))
            {
                CacheData = new JObject();
            }

            JObject cacheJObject = (JObject)CacheData;
            JToken currentToken = cacheJObject;

            // Navigate to the parent token
            for (int i = 0; i < paths.Length - 1; i++)
            {
                if (currentToken[paths[i]] == null || !(currentToken[paths[i]] is JObject))
                {
                    currentToken[paths[i]] = new JObject();
                }
                currentToken = currentToken[paths[i]];
            }

            // Set the value
            string key = paths[paths.Length - 1];
            if (key == "" || eventData.path == "/")
            {
                CacheData = JToken.FromObject(eventData.data);
            }
            else
            {
                ((JObject)currentToken)[key] = JToken.FromObject(eventData.data);
            }

            if (_ValueChanged != null)
            {
                DatabaseReference databaseReference = new DatabaseReference(Reference);
                FirebaseDataSnapshot snapshot = new FirebaseDataSnapshot(databaseReference, CacheData);
                ValueChangedEventArgs args = new ValueChangedEventArgs(snapshot, null);
                _ValueChanged(this, args);
            }
        }

        void ProcessEventPatchData(FirebaseServerEventData eventData)
        {
            if (CacheData == null || !(CacheData is JObject))
            {
                CacheData = new JObject();
            }

            JObject cacheJObject = (JObject)CacheData;
            JObject patchData = JObject.FromObject(eventData.data);

            string path = eventData.path.Trim('/');
            if (string.IsNullOrEmpty(path))
            {
                cacheJObject.Merge(patchData);
            }
            else
            {
                JToken target = cacheJObject.SelectToken(path.Replace('/', '.'));
                if (target is JObject targetObject)
                {
                    targetObject.Merge(patchData);
                }
                else
                {
                    // If the target doesn't exist, we might need to create it.
                    // This part can be complex depending on desired behavior.
                    // For now, we assume the path exists.
                }
            }

            if (_ValueChanged != null)
            {
                DatabaseReference databaseReference = new DatabaseReference(Reference);
                FirebaseDataSnapshot snapshot = new FirebaseDataSnapshot(databaseReference, CacheData);
                ValueChangedEventArgs args = new ValueChangedEventArgs(snapshot, null);
                _ValueChanged(this, args);
            }
        }

        void DisposedUnityWebRequestIfNoReferences()
        {
            if (valueChangedRefCount == 0 &&
                childAddedRefCount == 0 &&
                childChangedRefCount == 0 &&
                childMovedRefCount == 0 &&
                childRemovedRefCount == 0)
            {
#if UNITY_WEBGL
                if (esGL != null)
                {
                    esGL.Close();
                    esGL = null;
                }
#else
                if (webReq != null)
                {
                    webReq.Dispose();
                    webReq = null;
                }
#endif
            }
        }

        public DatabaseReference(string reference)
        {
            this.reference = reference.Trim('/', ' ');
        }

        public string ReferenceUrl => FirebaseSettings.DATABASE_URL + reference + ".json";

        public string Reference => reference;

        public DatabaseReference Child(string node) => new(node.Trim('/', ' '));

        #region Query Methods

        public override Query EndAt(string value)
        {
            this.endAt = "endAt=" + "\"" + value + "\"";
            return this;
        }

        public override Query EndAt(double value)
        {
            this.endAt = "endAt=" + value;
            return this;
        }

        public override Query EndAt(bool value)
        {
            this.endAt = "endAt=" + value;
            return this;
        }

        public override Query EndAt(string value, string key)
        {
            this.orderBy = "orderBy=\"" + key + "\"";
            this.endAt = "endAt=\"" + value + "\"";
            return this;
        }

        public override Query EndAt(double value, string key)
        {
            this.orderBy = "orderBy=\"" + key + "\"";
            this.endAt = "endAt=" + value;
            return this;
        }

        public override Query EndAt(bool value, string key)
        {
            this.orderBy = "orderBy=\"" + key + "\"";
            this.endAt = "endAt=" + value;
            return this;
        }

        public override Query EqualTo(bool value, string key)
        {
            this.orderBy = "orderBy=\"" + key + "\"";
            this.equalTo = "equalTo=" + value;
            return this;
        }

        public override Query EqualTo(double value, string key)
        {
            this.orderBy = "orderBy=\"" + key + "\"";
            this.equalTo = "equalTo=" + value;
            return this;
        }

        public override Query EqualTo(string value, string key)
        {
            this.orderBy = "orderBy=\"" + key + "\"";
            this.equalTo = "equalTo=\"" + value + "\"";
            return this;
        }

        public override Query EqualTo(bool value)
        {
            this.equalTo = "equalTo=" + value;
            return this;
        }

        public override Query EqualTo(double value)
        {
            this.equalTo = "equalTo=" + value;
            return this;
        }

        public override Query EqualTo(string value)
        {
            this.equalTo = "equalTo=\"" + value + "\"";
            return this;
        }

        public override Query LimitToFirst(int limit)
        {
            this.limitToFirst = "limitToFirst=" + limit;
            return this;
        }

        public override Query LimitToLast(int limit)
        {
            this.limitToLast = "limitToLast=" + limit;
            return this;
        }

        public override Query OrderByChild(string path)
        {
            this.orderBy = "orderBy=\"" + path + "\"";
            return this;
        }

        public override Query OrderByKey()
        {
            this.orderBy = "orderBy=\"$key\"";
            return this;
        }

        public override Query OrderByPriority()
        {
            this.orderBy = "orderBy=\"$priority\"";
            return this;
        }

        public override Query OrderByValue()
        {
            this.orderBy = "orderBy=\"$value\"";
            return this;
        }

        public override Query StartAt(bool value, string key)
        {
            this.orderBy = "orderBy=\"" + key + "\"";
            this.startAt = "startAt=" + value;
            return this;
        }

        public override Query StartAt(double value, string key)
        {
            this.orderBy = "orderBy=\"" + key + "\"";
            this.startAt = "startAt=" + value;
            return this;
        }

        public override Query StartAt(string value, string key)
        {
            this.orderBy = "orderBy=\"" + key + "\"";
            this.startAt = "startAt=\"" + value + "\"";
            return this;
        }

        public override Query StartAt(bool value)
        {
            this.startAt = "startAt=" + value;
            return this;
        }

        public override Query StartAt(double value)
        {
            this.startAt = "startAt=" + value;
            return this;
        }

        public override Query StartAt(string value)
        {
            this.startAt = "startAt=\"" + value + "\"";
            return this;
        }

        List<string> GetQueries()
        {
            List<string> queries = new List<string>();
            if (orderBy != null)
                queries.Add(orderBy);
            if (startAt != null)
                queries.Add(startAt);
            if (endAt != null)
                queries.Add(endAt);
            if (equalTo != null)
                queries.Add(equalTo);
            if (limitToFirst != null)
                queries.Add(limitToFirst);
            if (limitToLast != null)
                queries.Add(limitToLast);
            return queries;
        }

        #endregion

        #region Async Database Operations

        public override async Task<Response<DataSnapshot>> GetValue(int timeout)
        {
            List<string> query = GetQueries();
            string url = this.ReferenceUrl;
            if (query != null && query.Count > 0)
            {
                url = url + "?" + string.Join("&", query.ToArray());
            }

            UnityWebRequest webReq = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
            webReq.downloadHandler = new DownloadHandlerBuffer();
            webReq.timeout = timeout;

            if (FirebaseAuth.Instance.IsSignedIn)
            {
                string accessToken = await FirebaseAuth.Instance.GetAccessToken();
                string sign = (query == null || query.Count == 0) ? "?" : "&";
                webReq.url = webReq.url + sign + "auth=" + accessToken;
            }

            var op = webReq.SendWebRequest();
            await op;

            if (webReq.result == UnityWebRequest.Result.ConnectionError)
            {
                return new Response<DataSnapshot>(webReq.error, false, 0, null);
            }
            else if (webReq.result == UnityWebRequest.Result.ProtocolError)
            {
                var res = JsonConvert.DeserializeObject<JObject>(webReq.downloadHandler.text);
                return new Response<DataSnapshot>(res["error"].ToString(), false, (int)webReq.responseCode, null);
            }
            else
            {
                var snapshotData = JsonConvert.DeserializeObject(webReq.downloadHandler.text);
                FirebaseDataSnapshot snapshot = new FirebaseDataSnapshot(this, snapshotData);
                return new Response<DataSnapshot>("success", true, (int)ResponseCode.SUCCESS, snapshot);
            }
        }

        public async Task<Response<string>> Push(object data, int timeout)
        {
            string rawData = JsonConvert.SerializeObject(data);
            UnityWebRequest webReq = new UnityWebRequest(this.ReferenceUrl, "POST");
            webReq.downloadHandler = new DownloadHandlerBuffer();
            byte[] bodyRaw = Encoding.UTF8.GetBytes(rawData);
            webReq.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webReq.SetRequestHeader("Content-Type", "application/json");
            webReq.timeout = timeout;

            if (FirebaseAuth.Instance.IsSignedIn)
            {
                string accessToken = await FirebaseAuth.Instance.GetAccessToken();
                webReq.url = webReq.url + "?auth=" + accessToken;
            }

            var op = webReq.SendWebRequest();
            await op;

            if (webReq.result == UnityWebRequest.Result.ConnectionError)
            {
                return new Response<string>(webReq.error, false, 0, null);
            }
            else if (webReq.result == UnityWebRequest.Result.ProtocolError)
            {
                var res = JsonConvert.DeserializeObject<JObject>(webReq.downloadHandler.text);
                return new Response<string>(res["error"].ToString(), false, (int)webReq.responseCode, null);
            }
            else
            {
                var dataMap = JsonConvert.DeserializeObject<JObject>(webReq.downloadHandler.text);
                string pushedId = dataMap["name"].ToString();
                return new Response<string>("success", true, (int)ResponseCode.SUCCESS, pushedId);
            }
        }

        public async Task<Response> SetRawJsonValue(string json, int timeout)
        {
            try
            {
                // Validate the json
                JToken.Parse(json);
                return await WriteFirebaseData(this.ReferenceUrl, json, timeout, "PUT");
            }
            catch (JsonReaderException)
            {
                throw new Exception("Not a valid json");
            }
        }

        public async Task<Response> SetValue(object data, int timeout = 10)
        {
            try
            {
                string json = JsonConvert.SerializeObject(data);
                return await WriteFirebaseData(this.ReferenceUrl, json, timeout, "PUT");
            }
            catch
            {
                throw new NotSupportedException("Not supported data types");
            }
        }

        public async Task<Response> UpdateChild(Dictionary<string, object> data, int timeout = 10)
        {
            string json = JsonConvert.SerializeObject(data);
            return await WriteFirebaseData(this.ReferenceUrl, json, timeout, "PATCH");
        }

        public async Task<Response> RemoveValue(int timeout)
        {
            UnityWebRequest webReq = new UnityWebRequest(this.ReferenceUrl, "DELETE");
            webReq.downloadHandler = new DownloadHandlerBuffer();
            webReq.SetRequestHeader("Content-Type", "application/json");
            webReq.timeout = timeout;

            if (FirebaseAuth.Instance.IsSignedIn)
            {
                string accessToken = await FirebaseAuth.Instance.GetAccessToken();
                webReq.url = webReq.url + "?auth=" + accessToken;
            }

            var op = webReq.SendWebRequest();
            await op;

            if (webReq.result == UnityWebRequest.Result.ConnectionError)
                return new Response("Network error: " + webReq.error, false, 0, null);
            else if (webReq.result == UnityWebRequest.Result.ProtocolError)
                return new Response("HTTP Error: " + webReq.downloadHandler.text, false, (int)webReq.responseCode, null);
            else
                return new Response("success", true, (int)ResponseCode.SUCCESS, webReq.downloadHandler.text);
        }

        private async Task<Response> WriteFirebaseData(string dbpath, object data, int timeout, string requestMethod)
        {
            UnityWebRequest webReq = new UnityWebRequest(dbpath, requestMethod);
            webReq.downloadHandler = new DownloadHandlerBuffer();
            byte[] bodyRaw = Encoding.UTF8.GetBytes(data.ToString());
            webReq.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webReq.SetRequestHeader("Content-Type", "application/json");
            webReq.timeout = timeout;

            if (FirebaseAuth.Instance.IsSignedIn)
            {
                string accessToken = await FirebaseAuth.Instance.GetAccessToken();
                webReq.url = webReq.url + "?auth=" + accessToken;
            }

            var op = webReq.SendWebRequest();
            await op;

            if (webReq.result == UnityWebRequest.Result.ConnectionError)
                return new Response(webReq.error, false, 0, null);
            else if (webReq.result == UnityWebRequest.Result.ProtocolError)
                return new Response("HTTP Error: " + webReq.downloadHandler.text, false, (int)webReq.responseCode, null);
            else
                return new Response("success", true, (int)ResponseCode.SUCCESS, webReq.downloadHandler.text);
        }

        #endregion

        public override void Dispose()
        {
            if (webReq != null)
                webReq.Dispose();
            CacheData = null;
            _ValueChanged = null;
            Disposed?.Invoke(this, EventArgs.Empty);
        }
    }
}
