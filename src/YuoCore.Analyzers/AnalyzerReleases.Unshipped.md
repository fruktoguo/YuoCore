; Unshipped analyzer releases

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
YUO0001 | Usage | Error | YuoTask calls must be awaited, returned, assigned, explicitly passed, or ended with Forget/Coroutine.
YUO0002 | Usage | Error | async void is forbidden except event-handler-like signatures.
YUO0003 | Usage | Error | YuoTask should not be synchronously consumed through GetAwaiter().GetResult(), including the AsTask().GetAwaiter().GetResult() bridge.
YUO0004 | Usage | Error | YuoTask calls must not be stored into fields or properties.
YUO0005 | Usage | Error | YuoTask local variables must not be awaited more than once in the same function-like scope.
YUO0006 | Architecture | Error | Gateway dispatcher implementations must not directly reference ECS types; they should translate client input into command or Actor messages.
YUO0007 | Architecture | Error | Protocol contracts must not reference runtime ownership or routing types such as ECS, Actor, Runtime, Location or Gateway objects.
