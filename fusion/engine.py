"""Target-scoped Windows-MCP labels over AgentMouse observations and input."""
import time


class FusionSession:
    def __init__(self, client, controls, now=time.monotonic):
        self.client, self.controls, self.now = client, controls, now
        self.observation = None
        self.busy = False

    async def snapshot(self, window_id=None, use_ui_tree=True):
        if self.busy:
            raise ValueError("input_busy: wait for the active gesture or pause it")
        self.observation = None
        tree = {"elements": []}
        if window_id and not window_id.startswith("browser:") and use_ui_tree:
            tree = await self.controls(window_id)
        result = await self.client.call("observe", **({"windowId": window_id, "capture": "window"} if window_id else {}))
        window = result.get("window") or {}
        if tree.get("pid"):
            bounds = window.get("bounds", {})
            actual = [bounds.get("x"), bounds.get("y"), bounds.get("x", 0) + bounds.get("width", 0), bounds.get("y", 0) + bounds.get("height", 0)]
            if tree["pid"] != window.get("pid") or list(tree.get("bounds", [])) != actual:
                tree = {"elements": [], "error": "window_changed: control identities/bounds differ from the captured window"}
        result["controls"] = tree
        self.observation = {"window_id": window_id, "token": result["observationId"], "at": self.now(), "result": result}
        return result

    def resolve(self, window_id, observation_id, loc=None, label=None):
        observation = self.observation
        if not observation or observation_id != observation["token"] or self.now() - observation["at"] > 15:
            raise ValueError("stale_observation: take a fresh Snapshot/Screenshot")
        if not window_id or window_id != observation["window_id"]:
            raise ValueError("wrong_target: the action must address the same explicit window as its image")
        if observation["result"].get("inputAllowed") is False:
            raise ValueError("unverified_frame: target has no new frame after input; stop retries")
        if (loc is None) == (label is None):
            raise ValueError("Provide exactly one of loc or label")
        if label is not None:
            elements = observation["result"]["controls"]["elements"]
            matches = [item for item in elements if item["label"] == label]
            if len(matches) != 1:
                raise ValueError("Unknown label in this window observation")
            center = matches[0]["center"]
            loc = [center["x"], center["y"]]
        if not isinstance(loc, (list, tuple)) or len(loc) != 2 or any(type(value) is not int for value in loc):
            raise ValueError("loc must contain two integer coordinates in the returned coordinateSpace")
        return {"windowId": window_id, "observationId": observation_id, "x": loc[0], "y": loc[1]}

    async def action(self, method, window_id, observation_id, loc=None, label=None, **options):
        if self.busy:
            raise ValueError("input_busy")
        arguments = self.resolve(window_id, observation_id, loc, label)
        self.observation = None
        self.busy = True
        try:
            return await self.client.call(method, **arguments, **options)
        finally:
            self.busy = False

    async def pause(self):
        self.observation = None
        return await self.client.call("pause")
