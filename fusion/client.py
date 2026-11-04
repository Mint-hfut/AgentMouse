"""One asynchronous, cancellable connection to the existing AgentMouse engine."""
import asyncio
import json
import subprocess


class AgentMouseClient:
    def __init__(self, node, runtime):
        self.node, self.runtime = node, runtime
        self.process = None
        self.pending = {}
        self.sequence = 0
        self.start_lock = asyncio.Lock()

    async def start(self):
        async with self.start_lock:
            if self.process is None:
                self.process = await asyncio.create_subprocess_exec(
                    self.node, str(self.runtime / "fusion-worker.mjs"),
                    stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE,
                    limit=32 * 1024 * 1024, creationflags=subprocess.CREATE_NO_WINDOW,
                )
                self.reader = asyncio.create_task(self.read())

    async def read(self):
        try:
            while line := await self.process.stdout.readline():
                result = json.loads(line)
                future = self.pending.pop(result.get("id"), None)
                if future and not future.done():
                    future.set_result(result)
        finally:
            for future in self.pending.values():
                if not future.done():
                    future.set_exception(RuntimeError("AgentMouse worker disconnected; action outcome may be unknown"))
            self.pending.clear()

    async def call(self, method, **arguments):
        await self.start()
        self.sequence += 1
        identity = self.sequence
        future = asyncio.get_running_loop().create_future()
        self.pending[identity] = future
        self.process.stdin.write((json.dumps({"id": identity, "method": method, **arguments}) + "\n").encode())
        await self.process.stdin.drain()
        try:
            response = await asyncio.wait_for(future, 30)
            if not response.get("ok"):
                error = response.get("error", {})
                raise ValueError(f"{error.get('code')}: {error.get('message')}")
            return response["result"]
        except (asyncio.CancelledError, TimeoutError):
            if method in {"click", "drag", "scroll", "move"}:
                try:
                    await asyncio.shield(self.call("pause"))
                except Exception:
                    pass
            raise
        finally:
            self.pending.pop(identity, None)

    async def close(self):
        if self.process:
            self.process.stdin.close()
            try:
                await asyncio.wait_for(self.process.wait(), 8)
            except TimeoutError:
                self.process.kill()
                await self.process.wait()
            await self.reader
