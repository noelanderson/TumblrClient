# TumblrClient
A minimalist client for authenticated access to Tumblr's [V2 API](https://www.tumblr.com/docs/en/api/v2)

# Installing
Search for `TumblrClient` in [NuGet](https://www.nuget.org/packages/TumblrClient/), or use your Package Manager Console.

```
PM> Install-Package TumblrClient
```

# Code Examples

A stateful class `TumblrClient` provides methods to create, read, update & delete posts.  The class also tracks the authentication  state and will do a OAuth 1.0a exchange if the called method requires it.

The Tumblr API uses three different levels of authentication, depending on the method.

1. None: No authentication. Anybody can query the method.
1. API key: Requires an API key.
1. OAuth: Requires a signed request that meets the Tumblr version of the OAuth 1.0a Protocol.

To get a Tumblr API Key (Consumer key & Secret) Register an application at [Tumblr Applications]( https://www.tumblr.com/oauth/apps)

The Key & Secret are used when constructing the TumblrClient class.
If the method called needs user authentication this will be automatically triggered on the first call needing it.  This will get the necessary tokens from Tumblr and will pop a browser window for the user to grant permissions.

All methods carry content in the [Tumblr Neue Post Format](https://www.tumblr.com/docs/npf)  


### Get all posts

```cs
using (TumblrClient tumblrClient = new TumblrClient(httpClient, Key, Secret))
{
    JArray publicPosts = await tumblrClient.GetPosts("yourblog", true);
    JArray draftPosts = await tumblrClient.GetDrafts(options.Blog);
    JArray queuedPosts = await tumblrClient.GetQueue(options.Blog);
}
```
*Note that `GetPosts` can be called with or without user auth.  The key difference is that if the target blog is owned by the authenticated user, calling `GetPosts` with user authentication will also return posts that are marked as private or that have been censored from public display by Tumblr. Otherwise you get just the public posts.*
*`GetDrafts` and `GetQueue` always require user authentication.*

### Create a post
```cs
string json = @"{
        'state': 'published',
        'tags': 'test,tumblr.client',
        'content': [
            {
                'type': 'text',
                'text': 'A sample text post'
            },
            {
                'type': 'text',
                'text': 'some red text plus strikethrough',
                'formatting': [
                    {
                        'type': 'color',
                        'start': 0,
                        'end': 32,
                        'hex': '#ff492f'
                    },
                    {
                        'type': 'strikethrough',
                        'start': 19,
                        'end': 32
                    }
                ]
            },
        ]
    }";
JToken post = JToken.Parse(jpost);
long Id = await tumblrClient.CreatePost("yourblog", post);
```

### Get a specific post by Id
```cs
post = await tumblrClient.GetPost("yourblog", Id, true);
```

### Update an existing post
```cs
string updatedJson = @"{
        'tags': 'edited,tumblr.client',
        'content': [
            {
                'type': 'text',
                'text': 'Updated post',
                'formatting': [
                    {
                        'type': 'bold',
                        'start': 0,
                        'end': 12
                    }
                ]
            }
        ]
    }";
post = JToken.Parse(updatedJson);
Id = await tumblrClient.UpdatePost("yourblog", Id, post);
```

### Delete a post
```cs
bool result = await tumblrClient.DeletePost("yourblog", Id);
```
