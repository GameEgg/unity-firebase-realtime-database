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
    public static class UnityWebRequestExtensions
    {
        public static Task<UnityWebRequestAsyncOperation> SendWebRequest(this UnityWebRequest webRequest)
        {
            var tcs = new TaskCompletionSource<UnityWebRequestAsyncOperation>();
            UnityWebRequestAsyncOperation op = webRequest.SendWebRequest();
            op.completed += _ => tcs.SetResult(op);
            return tcs.Task;
        }
    }

    public class FirebaseAuth : MonoBehaviour
    {
        readonly string EMAIL_AUTH_URL = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=" + FirebaseSettings.WEB_API;
        readonly string CUSTOM_TOKEN_AUTH_URL = "https://www.googleapis.com/identitytoolkit/v3/relyingparty/verifyCustomToken?key=" + FirebaseSettings.WEB_API;
        readonly string ANONYMOUS_AUTH_URL = "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + FirebaseSettings.WEB_API;
        readonly string REFRESH_TOKEN_URL = "https://securetoken.googleapis.com/v1/token?key=" + FirebaseSettings.WEB_API;
        readonly string USER_INFO_URL = "https://www.googleapis.com/identitytoolkit/v3/relyingparty/getAccountInfo?key=" + FirebaseSettings.WEB_API;

        TokenData tokenData;
        string localId;
        public TokenData CurrentTokenData => tokenData;
        public string CurrentLocalId => localId;

        private static bool applicationIsQuitting = false;
        private static FirebaseAuth _instance;
        private static object _lock = new object();
        public static FirebaseAuth Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = (FirebaseAuth)FindFirstObjectByType(typeof(FirebaseAuth));

                        if (FindObjectsByType<FirebaseAuth>(FindObjectsSortMode.None).Length > 1)
                        {
                            Debug.LogError("there should never be more than 1 singleton!");
                            return _instance;
                        }

                        if (_instance == null)
                        {
                            GameObject singleton = new GameObject();
                            _instance = singleton.AddComponent<FirebaseAuth>();
                            singleton.name = "(singleton) " + typeof(FirebaseAuth).ToString();
                            DontDestroyOnLoad(singleton);
                            Debug.Log(typeof(FirebaseAuth).ToString() + " singleton created");
                        }
                        else
                        {
                            Debug.Log("instance already created: " + _instance.gameObject.name);
                        }
                    }
                    return _instance;
                }
            }
        }

        void Awake()
        {
            applicationIsQuitting = false;
        }

        void OnDestroy()
        {
            applicationIsQuitting = true;
        }

        public bool IsSignedIn => tokenData != null;

        // GetAccessToken을 async/await 패턴으로 변경
        public async Task<string> GetAccessToken()
        {
            if (tokenData == null)
                return null;
            else if (IsTokenExpired)
            {
                // RefreshAccessTokenAsync 내부에서 tokenData를 갱신함
                await RefreshAccessToken(10);
                return tokenData.IdToken;
            }
            else
                return tokenData.IdToken;
        }

        private bool IsTokenExpired => DateTime.Now - tokenData.RefreshedAt > TimeSpan.FromSeconds(double.Parse(tokenData.ExpiresIn));

        public async Task<List<UserData>> FetchUserInfo(int timeout)
        {
            if (tokenData == null)
                throw new Exception("User has not logged in");

            UnityWebRequestAsyncOperation op = StartRequest(USER_INFO_URL, "POST", new Dictionary<string, object>(){
                {"idToken", tokenData.IdToken}
            }, timeout);
            await op;

            if (op.webRequest.result == UnityWebRequest.Result.ConnectionError)
                throw new Exception(op.webRequest.error);
            if (op.webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                var errorObject = JsonConvert.DeserializeObject<JObject>(op.webRequest.downloadHandler.text);
                throw new Exception(errorObject["error"]["message"].ToString());
            }

            var responseObject = JsonConvert.DeserializeObject<JObject>(op.webRequest.downloadHandler.text);
            var usersArray = (JArray)responseObject["users"];
            return usersArray.ToObject<List<UserData>>();
        }

        public async Task<TokenData> RefreshAccessToken(int timeout)
        {
            if (tokenData == null)
                throw new Exception("User has not logged in");

            UnityWebRequestAsyncOperation op = StartRequest(REFRESH_TOKEN_URL, "POST", new Dictionary<string, object>(){
                {"grant_type", "refresh_token"},
                {"refresh_token", tokenData.RefreshToken}
            }, timeout);
            
            await op;

            if (op.webRequest.result == UnityWebRequest.Result.ConnectionError)
                throw new Exception(op.webRequest.error);
            if (op.webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                var errorObject = JsonConvert.DeserializeObject<JObject>(op.webRequest.downloadHandler.text);
                throw new Exception(errorObject["error"]["message"].ToString());
            }

            var dataMap = JsonConvert.DeserializeObject<Dictionary<string, object>>(op.webRequest.downloadHandler.text);
            tokenData = new TokenData(
                dataMap["id_token"].ToString(),
                dataMap["refresh_token"].ToString(),
                dataMap["expires_in"].ToString(),
                DateTime.Now
            );
            return tokenData;
        }

        public async Task<TokenData> SignInWithCustomToken(string customToken, int timeout)
        {
            UnityWebRequestAsyncOperation op = StartRequest(CUSTOM_TOKEN_AUTH_URL, "POST", new Dictionary<string, object>(){
                {"token", customToken},
                {"returnSecureToken", true}
            }, timeout);

            await op;

            return HandleFirebaseSignInResponse(op);
        }

        public async Task<TokenData> SignInWithEmail(string email, string password, int timeout = 10)
        {
            UnityWebRequestAsyncOperation op = StartRequest(EMAIL_AUTH_URL, "POST", new Dictionary<string, object>(){
                {"email", email},
                {"password", password},
                {"returnSecureToken", true}
            }, timeout);

            await op;

            return HandleFirebaseSignInResponse(op);
        }

        public async Task<TokenData> CreateUserWithEmailAndPassword(string email, string password, string displayName, int timeout)
        {
            UnityWebRequestAsyncOperation op = StartRequest(ANONYMOUS_AUTH_URL, "POST", new Dictionary<string, object>(){
                {"email", email},
                {"password", password},
                {"returnSecureToken", true}
            }, timeout);

            await op;

            return HandleFirebaseSignInResponse(op);
        }

        public async Task<TokenData> SignInAnonymously(int timeout)
        {
            UnityWebRequestAsyncOperation op = StartRequest(ANONYMOUS_AUTH_URL, "POST", new Dictionary<string, object>(){
                {"returnSecureToken", true}
            }, timeout);

            await op;

            return HandleFirebaseSignInResponse(op);
        }

        UnityWebRequestAsyncOperation StartRequest(string url, string requestMethod, Dictionary<string, object> data, int timeout)
        {
            UnityWebRequest webReq = new UnityWebRequest(url, requestMethod);
            webReq.downloadHandler = new DownloadHandlerBuffer();
            byte[] bodyRaw = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));
            webReq.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webReq.SetRequestHeader("Content-Type", "application/json");
            webReq.timeout = timeout;
            return webReq.SendWebRequest();
        }

        TokenData HandleFirebaseSignInResponse(UnityWebRequestAsyncOperation op)
        {
            if (op.webRequest.result == UnityWebRequest.Result.ConnectionError)
                throw new Exception(op.webRequest.error);
            else if (op.webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                var errorObject = JsonConvert.DeserializeObject<JObject>(op.webRequest.downloadHandler.text);
                throw new Exception(errorObject["error"]["message"].ToString());
            }
            else
            {
                var dataMap = JsonConvert.DeserializeObject<Dictionary<string, string>>(op.webRequest.downloadHandler.text);
                tokenData = new TokenData(
                    dataMap["idToken"],
                    dataMap["refreshToken"],
                    dataMap["expiresIn"],
                    DateTime.Now
                );
                localId = dataMap["localId"];
                return tokenData;
            }
        }
    }
}
