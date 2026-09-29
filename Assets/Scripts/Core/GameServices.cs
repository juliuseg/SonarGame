using UnityEngine;

public static class GameServices
{
    public static ObjectResolver Resolver { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStatics()
    {
        Resolver = null;
    }

    // Systems may come up in any order, so whichever runs first creates the resolver.
    public static ObjectResolver EnsureInitialized()
    {
        return Resolver ??= new ObjectResolver();
    }
}
