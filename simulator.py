import argparse
import asyncio
import math
from datetime import datetime, timezone

from asyncua import Server, ua


def auto_scenario(tick):
    phase = tick % 42
    if 18 <= phase < 27:
        return "jam"
    if 31 <= phase < 39:
        return "sensor"
    return "normal"


async def run(selected_scenario, port):
    server = Server()
    await server.init()
    endpoint = f"opc.tcp://127.0.0.1:{port}/packaging/"
    server.set_endpoint(endpoint)
    server.set_server_name("Packaging Line Simulator")
    server.set_security_policy([ua.SecurityPolicyType.NoSecurity])
    server.set_identity_tokens([ua.AnonymousIdentityToken])
    namespace = await server.register_namespace("urn:packaging-line:training")
    line = await server.nodes.objects.add_object(namespace, "PackagingLine")

    definitions = {
        "ConveyorRunning": (True, ua.VariantType.Boolean),
        "LineSpeed": (60.0, ua.VariantType.Double),
        "PackCount": (0, ua.VariantType.Int64),
        "JamDetected": (False, ua.VariantType.Boolean),
    }
    nodes = {}
    for name, (initial, data_type) in definitions.items():
        nodes[name] = await line.add_variable(
            ua.NodeId(name, namespace), ua.QualifiedName(name, namespace), initial, data_type
        )

    pack_count = 0
    fraction = 0.0
    tick = 0
    previous = None
    async with server:
        print(f"OPC UA listening on {endpoint}", flush=True)
        while True:
            scenario = auto_scenario(tick) if selected_scenario == "auto" else selected_scenario
            if scenario != previous:
                print(f"Scenario: {scenario}", flush=True)
                previous = scenario

            jam = scenario == "jam"
            speed = 0.0 if jam else round(60 + 4 * math.sin(tick / 4), 1)
            fraction += speed / 60
            complete = int(fraction)
            pack_count += complete
            fraction -= complete
            values = (not jam, speed, pack_count, jam)
            timestamp = datetime.now(timezone.utc)

            # In the sensor scenario the conveyor still runs, but its speed tag is Bad.
            for (name, (_, data_type)), value in zip(definitions.items(), values):
                bad = scenario == "sensor" and name == "LineSpeed"
                status = ua.StatusCodes.BadSensorFailure if bad else ua.StatusCodes.Good
                reading = ua.DataValue(
                    Value=ua.Variant(value, data_type),
                    StatusCode_=ua.StatusCode(status),
                    SourceTimestamp=timestamp,
                )
                await nodes[name].write_value(reading)

            tick += 1
            await asyncio.sleep(1)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Simulate a nonwovens packaging line.")
    parser.add_argument("--scenario", choices=["auto", "normal", "jam", "sensor"], default="auto")
    parser.add_argument("--port", type=int, default=4848)
    args = parser.parse_args()
    try:
        asyncio.run(run(args.scenario, args.port))
    except KeyboardInterrupt:
        pass
