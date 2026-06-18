namespace NodeKit.Grpc
{
    internal enum BuildEventKind
    {
        Log,
        JobCreated,
        JobRunning,
        RegistryPushSucceeded,
        DigestAcquired,
        Succeeded,
        Failed,
    }
}
