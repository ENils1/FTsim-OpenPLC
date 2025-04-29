# FTsim-OpenPLC
![Skjermbilde 2025-02-06 125758](https://github.com/user-attachments/assets/fab16526-bf24-47c7-a8c1-2de4f054a633)

FTsim-OpenPLC is a Unity simulation tool for Fischertechnik training models that uses WebSocket communication to interface with an OpenPLC Runtime server. It currently supports only **Indexed Line with two machine stations**.

## Features

- **Indexed Line Simulation:** Only supports an Indexed Line with two machine stations.
- **WebSocket Communication:** Data exchange with an OpenPLC server using WebSocket and JSON.
- **OpenPLC Runtime PSM:** OpenPLC Runtime PSM driver.

## Getting Started

### Prerequisites

- **OpenPLC Runtime:** An operational OpenPLC server with Websocket connectivity.

### Configuration

- Edit the configuration file at **FTsim-OpenPLC_Data\StreamingAssets\config-L2N.json** to set your OpenPLC connection details and I/O addresses.

## OpenPLC Python SubModule (PSM)
Driver for OpenPLC Runtime. 

#### Install package: websocket-server

Raspberry Pi terminal:
```
source OpenPLC_v3/.venv/bin/activate
pip install websocket-server
```
Copy the driver to the hardware section of the OpenPLC-Runtime, select PSM. 
```
from websocket_server import WebsocketServer
import json
import time
import threading
import psm

HOST = "0.0.0.0"
PORT = 5000
UPDATE_INTERVAL = 0.1
NUMBER_OF_OUTPUTS = 10

def init_psm():
    for addr in ["IX1.0", "IX0.6", "IX0.7", "IX1.1", "IX1.2", "IX0.3", "IX0.5"]:
        psm.set_var(addr, True)

def get_qx_updates():
    qx = {}
    for i in range(NUMBER_OF_OUTPUTS):
        addr = f"QX{i//8}.{i%8}"
        try:
            qx[addr] = psm.get_var(addr)
        except Exception:
            qx[addr] = None
    return qx

def new_client(client, server):
    print(f"Client connected: {client['id']}")

def client_left(client, server):
    print(f"Client disconnected: {client['id']}")

def message_received(client, server, message):
    try:
        cmd = json.loads(message)
        if cmd.get("action") == "set_IX":
            psm.set_var(cmd.get("address"), cmd.get("value"))
    except Exception as e:
        print(f"Error: {e}")

def broadcast_loop(server):
    while True:
        try:
            data = json.dumps(get_qx_updates())
            server.send_message_to_all(data)
        except Exception as e:
            print(f"Broadcast error: {e}")
        time.sleep(UPDATE_INTERVAL)

def main():
    psm.start()
    init_psm()

    server = WebsocketServer(host=HOST, port=PORT)
    server.set_fn_new_client(new_client)
    server.set_fn_client_left(client_left)
    server.set_fn_message_received(message_received)

    threading.Thread(target=broadcast_loop, args=(server,), daemon=True).start()
    print(f"Server running on {HOST}:{PORT}")
    server.run_forever()

if __name__ == "__main__":
    main()
```

## Credits

FTsim-OpenPLC build on the work of [FTsim-ADS](https://github.com/laspp/FTsim-ADS).

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
