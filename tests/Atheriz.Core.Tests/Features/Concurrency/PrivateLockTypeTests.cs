using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Server.Hosting;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Private lock objects converted to System.Threading.Lock (non-reentrant:
// same-thread re-entry throws instead of deadlocking). Each pin asserts the
// converted field type so a silent revert to object fails loudly. Behavior
// (mutual exclusion) is covered by the surrounding concurrency suites.
// The six throttle dict+Lock pairs live in ThrottledLog holder instances now,
// so those sites pin the holder field type plus the holder's single lock.
public sealed class PrivateLockTypeTests
{
    private static void AssertPrivateLockFieldIsLock(Type declaringType, string fieldName)
    {
        var field = declaringType.GetField(
            fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(typeof(System.Threading.Lock), field.FieldType);
    }

    private static void AssertPrivateHolderIsThrottledLog(Type declaringType, string fieldName)
    {
        var field = declaringType.GetField(
            fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(typeof(ThrottledLog), field.FieldType);
    }

    [Fact]
    public void PendingLimiter_InternalLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(PendingLimiter), "_lock");
    }

    [Fact]
    public void Command_ParserLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(Command), "_parserLock");
    }

    [Fact]
    public void BoundedDictionary_InternalLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(ObjectRegistry.BoundedDictionary<string, string>), "_lock");
    }

    [Fact]
    public void FileLoggerProvider_InternalLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(FileLoggerProvider), "_lock");
    }

    [Fact]
    public void TelnetStreamWriter_WriteLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(TelnetStreamWriter), "_writeLock");
    }

    [Fact]
    public void Channel_HistoryLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(Channel), "_histLock");
    }

    [Fact]
    public void AtherizLogger_FactoryLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(AtherizLogger), "_lock");
    }

    [Fact]
    public void AtherizLogger_FileLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(AtherizLogger), "_fileLock");
    }

    [Fact]
    public void GameTime_OwnedLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(GameTime), "_ownedLock");
    }

    [Fact]
    public void GameTime_StartLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(GameTime), "_startLock");
    }

    [Fact]
    public void ThrottledLog_InternalLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(ThrottledLog), "_lock");
    }

    [Fact]
    public void WebSocketHandler_OversizeThrottle_IsHolder()
    {
        AssertPrivateHolderIsThrottledLog(typeof(WebSocketHandler), "_wsOversizeLog");
    }

    [Fact]
    public void BaseConnection_RetryDrainDropThrottle_IsHolder()
    {
        AssertPrivateHolderIsThrottledLog(typeof(BaseConnection), "_retryDrainDropLog");
    }

    [Fact]
    public void WebSocketProtocol_OversizeThrottle_IsHolder()
    {
        AssertPrivateHolderIsThrottledLog(typeof(WebSocketProtocol), "_oversizeLog");
    }

    [Fact]
    public void TelnetProtocol_OverlongDropThrottle_IsHolder()
    {
        AssertPrivateHolderIsThrottledLog(typeof(TelnetProtocol), "_overlongDropLog");
    }

    [Fact]
    public void ConnectionManager_MalformedThrottle_IsHolder()
    {
        AssertPrivateHolderIsThrottledLog(typeof(ConnectionManager), "_malformedLog");
    }

    [Fact]
    public void ConnectionManager_OversizeThrottle_IsHolder()
    {
        AssertPrivateHolderIsThrottledLog(typeof(ConnectionManager), "_oversizeLog");
    }

    [Fact]
    public void ConnectionManager_GlobalLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(ConnectionManager), "_globalLock");
    }

    [Fact]
    public void ConnectionScreen_CacheLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(ConnectionScreen), "_lock");
    }

    [Fact]
    public void ServerEvents_CharCreateLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(ServerEvents), "_charCreateLock");
    }

    [Fact]
    public void GlobalTickerHolder_TickerLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(GlobalTickerHolder), "_lock");
    }

    [Fact]
    public void NodeHandler_CurrentLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(NodeHandler), "_currentLock");
    }

    [Fact]
    public void MapHandlerSingleton_InstanceLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(MapHandlerSingleton), "_lock");
    }

    [Fact]
    public void ChannelCommand_CacheLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(Atheriz.Core.Commands.LoggedIn.ChannelCommand), "CacheLock");
    }

    [Fact]
    public void CommandRegistry_RegistryLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(CommandRegistry), "Lock");
    }

    [Fact]
    public void AsyncTicker_SlotTableLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(Atheriz.Core.Concurrency.AsyncTicker), "_lock");
    }

    [Fact]
    public void TimeSlot_CoroLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(Atheriz.Core.Concurrency.AsyncTicker.TimeSlot), "_lock");
    }

    [Fact]
    public void AsyncThreadPool_WorkerLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(Atheriz.Core.Concurrency.AsyncThreadPool), "_lock");
    }

    [Fact]
    public void Node_PersistedSubtypeLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(Node), "_persistedSubtypeLock");
    }

    [Fact]
    public void DtoConverter_SubtypeLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(Atheriz.Core.Persistence.Converters.GameObjectDtoConverter), "_subtypeLock");
    }

    [Fact]
    public void AtherizSettings_GlobalLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(AtherizSettings), "_globalLock");
    }

    [Fact]
    public void AtherizDbContext_InitLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(AtherizDbContext), "_initLock");
    }

    [Fact]
    public void SaltProvider_SaltLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(SaltProvider), "_lock");
    }

    [Fact]
    public void Autosave_StateLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(Autosave), "_lock");
    }

    [Fact]
    public void StartStop_GameServerEventLock_IsLockType()
    {
        AssertPrivateLockFieldIsLock(typeof(StartStop), "_gameServerEventLock");
    }
}
