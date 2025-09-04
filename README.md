> ### Note
> This is a fork of the original [unity-firebase-realtime-database](https://github.com/edricwilliem/unity-firebase-realtime-database) repository by edricwilliem.
> 
> It has been modified to use the `async/await` pattern.

# Unity Firebase Realtime Database REST API
Write, Read, Remove and Streaming data using [Firebase's database REST API](https://firebase.google.com/docs/reference/rest/database)

This is not firebase's official plugins library.

Tested on Android and WebGL platform. should work well on other platforms too since most of the implementation is only a simple http REST request.

Contributions to this project are welcome!.

## Sample Usage

### Setting Firebase

Before using the library you need to setup some settings in `FirebaseSettings.cs`
```
DATABASE_URL = "https://example.firebaseio.com/";
WEB_API = "[WEB_API_KEY]";
```

### Write Data
Set Value:
```csharp
DatabaseReference reference = FirebaseDatabase.Instance.GetReference("path/to/save");
var response = await reference.SetValue("mydata");
if (response.success)
{
    Debug.Log("Write success");
}
else
{
    Debug.Log("Write failed: " + response.message);
}

DatabaseReference reference = FirebaseDatabase.Instance.GetReference("path/to/save");
var response = await reference.SetRawJsonValue("{\"key\":\"value\"}");
if (response.success)
{
    Debug.Log("Write success");
}
else
{
    Debug.Log("Write failed: " + response.message);
}
```
Update Child Value:
```csharp
DatabaseReference reference = FirebaseDatabase.Instance.GetReference("path/to/save");
var response = await reference.UpdateValue(new Dictionary<string, object>()
{
    {"child1", "value1"},
    {"child2", "value2"}
});

if (response.success)
{
    Debug.Log("Write success");
}
else
{
    Debug.Log("Write failed: " + response.message);
}
```
Push Value:
```csharp
DatabaseReference reference = FirebaseDatabase.Instance.GetReference("path/to/save");
var response = await reference.Push("mydata");
if (response.success)
{
    Debug.Log("Pushed with id: " + response.data);
}
else
{
    Debug.Log("Push failed: " + response.message);
}
```

### Read Data
Get Value:
```csharp
DatabaseReference reference = FirebaseDatabase.Instance.GetReference("path/to/query");
var response = await reference.GetValue();
if (response.success)
{
    Debug.Log("Success fetched data: " + response.data.GetRawJsonValue());
}
else
{
    Debug.Log("Fetch data failed: " + response.message);
}
```
Query & Order :

* OrderByChild
* OrderByKey
* OrderByValue
* StartAt
* EndAt
* LimitAtFirst
* LimitAtLast
```csharp
DatabaseReference reference = FirebaseDatabase.Instance.GetReference("path/to/query");
var response = await reference.OrderByChild("age").StartAt(12).EndAt(20).LimitAtFirst(5).GetValue();
if (response.success)
{
    Debug.Log("Success fetched data: " + response.data.GetRawJsonValue());
}
else
{
    Debug.Log("Fetch data failed: " + response.message);
}
```

### Delete Data
```csharp
DatabaseReference reference = FirebaseDatabase.Instance.GetReference("path/to/delete");
var response = await reference.RemoveValue();
if (response.success)
{
    Debug.Log("Delete data success");
}
else
{
    Debug.Log("Delete data failed: " + response.message);
}
```

### Streaming Data
```csharp
DatabaseReference reference = FirebaseDatabase.Instance.GetReference("path/to/stream");
reference.ValueChanged += (sender, e) =>
{
    Debug.Log(e.Snapshot.GetRawJsonValue());
};
reference.DatabaseError += (sender, e) =>
{
    Debug.Log(e.DatabaseError.Message);
    Debug.Log("Streaming connection closed");
};
```

### Authentication
Set the credential using saved tokens

```csharp
FirebaseAuth.Instance.TokenData = new TokenData()
{
    refreshToken = savedRefreshToken,
    idToken = savedAccessToken
};
```

or Sign In
```csharp
var tokenData = await FirebaseAuth.Instance.SignInWithEmail("example@example.com", "example");
```
after signed in, the `FirebaseAuth.Instance.CurrentTokenData` and  `FirebaseAuth.Instance.CurrentLocalId` will be automatically set 

## License
MIT
