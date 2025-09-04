using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FirebaseREST
{
    public partial class DatabaseReference
    {
        sealed class FirebaseDataSnapshot : DataSnapshot
        {
            private List<DataSnapshot> children;
            private bool alreadyParsed;

            public FirebaseDataSnapshot(DatabaseReference dbReference, object data) : base(dbReference, data)
            {
                this.dbReference = dbReference;
                this.data = data;
            }

            public override bool HasChildren
            {
                get
                {
                    if (data == null) return false;
                    if (data is JObject jObject)
                    {
                        return jObject.Count > 0;
                    }
                    return false;
                }
            }

            public override bool Exists
            {
                get
                {
                    return data != null;
                }
            }

            public override object Value
            {
                get
                {
                    return data;
                }
            }

            public override int ChildrenCount
            {
                get
                {
                    if (data == null)
                        return 0;
                    if (data is JObject jObject)
                    {
                        return jObject.Count;
                    }
                    return 0;
                }
            }

            public override DatabaseReference Reference
            {
                get
                {
                    return dbReference;
                }
            }

            public override string Key
            {
                get
                {
                    string[] arr = dbReference.Reference.Split('/');
                    return arr[arr.Length - 1];
                }
            }

            public override List<DataSnapshot> Children
            {
                get
                {
                    if (children != null) return this.children;
                    if (data == null) return null;

                    List<DataSnapshot> snapshot = new List<DataSnapshot>();
                    if (data is JObject jObject)
                    {
                        foreach (var child in jObject)
                        {
                            string referencePath = dbReference.Reference.TrimEnd('/', ' ') + "/" + child.Key;
                            snapshot.Add(new FirebaseDataSnapshot(new DatabaseReference(referencePath), child.Value));
                        }
                    }
                    this.children = snapshot;
                    return snapshot;
                }
            }

            public override DataSnapshot Child(string path)
            {
                if (HasChild(path))
                {
                    string referencePath = dbReference.Reference.TrimEnd('/', ' ') + "/" + path;
                    if (data is JObject jObject)
                    {
                        return new FirebaseDataSnapshot(new DatabaseReference(referencePath), jObject[path]);
                    }
                }
                return null;
            }

            public override string GetRawJsonValue()
            {
                if (data == null) return null;
                return JsonConvert.SerializeObject(data);
            }

            public override bool HasChild(string path)
            {
                if (data == null) return false;
                if (data is JObject jObject)
                {
                    return jObject.ContainsKey(path);
                }
                return false;
            }
        }
    }
}
