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
import logging
import socket
import psm

UPDATE_INTERVAL = 0.1
NUMBER_OF_OUTPUTS = 10
HOST = "0.0.0.0"
PORT = 5000

class WebSocketPLCServer:
    def __init__(self):
        self.server = WebsocketServer(host=HOST, port=PORT)
        self.server.socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.clients = {}  # {client_id: (client, last_heartbeat, active)}
        self.clients_lock = threading.Lock()
        self.running = False

    def start(self):
        """Start the WebSocket server and PSM"""
        try:
            psm.start()
            self.init_psm()
            self.running = True

            self.server.set_fn_new_client(self.new_client)
            self.server.set_fn_client_left(self.client_left)
            self.server.set_fn_message_received(self.message_received)

            threading.Thread(target=self.server.run_forever, daemon=True).start()
            threading.Thread(target=self.broadcast_updates, daemon=True).start()

            print(f"WebSocket server started on {HOST}:{PORT}")
        except Exception as e:
            print(f"Server startup error: {e}")
            self.stop()

    def init_psm(self):
        """Initialize PSM variables"""
        try:
            psm.set_var("IX1.0", True)
            psm.set_var("IX0.6", True)
            psm.set_var("IX0.7", True)
            psm.set_var("IX1.1", True)
            psm.set_var("IX1.2", True)
            psm.set_var("IX0.3", True)
            psm.set_var("IX0.5", True)
            print("PSM initialized")
        except Exception as e:
            print(f"PSM initialization error: {e}")

    def new_client(self, client, server):
        """Handle new client connection"""
        with self.clients_lock:
            # Record the time of connection.
            self.clients[client['id']] = (client, time.time(), True)
        print(f"New client connected: {client['id']}")

    def client_left(self, client, server):
        """Handle client disconnection"""
        with self.clients_lock:
            if client['id'] in self.clients:
                del self.clients[client['id']]
        print(f"Client disconnected: {client['id']}")

    def message_received(self, client, server, message):
        """Process incoming messages from client"""
        try:
            command = json.loads(message)
            #print(f"Received command: {command}")
            if command.get("action") == "set_IX":
                psm.set_var(command.get("address"), command.get("value"))
        except json.JSONDecodeError as e:
            print(f"Invalid JSON: {e} - Data: {message}")
        except Exception as e:
            print(f"Processing error: {e}")

    def broadcast_updates(self):
        """Broadcast QX updates to all clients"""
        while self.running:
            try:
                qx_updates = self.get_qx_updates()
                json_update = json.dumps(qx_updates)
                data_size = len(json_update.encode('utf-8'))
                with self.clients_lock:
                    for client_id in list(self.clients.keys()):
                        client, _, active = self.clients[client_id]
                        if active:
                            self.server.send_message(client, json_update)
            except Exception as e:
                print(f"Broadcast error: {e}")
                time.sleep(1)
            time.sleep(UPDATE_INTERVAL)

    def get_qx_updates(self):
        """Generate QX updates from PSM values"""
        qx_updates = {}
        for index in range(NUMBER_OF_OUTPUTS):
            msb = index // 8
            lsb = index % 8
            address = f"QX{msb}.{lsb}"
            try:
                qx_updates[address] = psm.get_var(address)
            except Exception as e:
                print(f"Error getting variable {address}: {e}")
        return qx_updates
    
    def stop(self):
        """Stop the server"""
        self.running = False
        self.server.shutdown()
        try:
            psm.stop()
        except Exception as e:
            print(f"Error stopping PSM: {e}")
        print("Server stopped")

def main():
    server = WebSocketPLCServer()
    server.start()
    while (not psm.should_quit()):
        time.sleep(0.1)
    server.stop()

if __name__ == "__main__":
    main()

```

## Credits

FTsim-OpenPLC build on the work of [FTsim-ADS](https://github.com/laspp/FTsim-ADS).

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
