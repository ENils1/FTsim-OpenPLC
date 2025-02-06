# FTsim-OpenPLC

FTsim-OpenPLC is a Unity simulation tool for Fischertechnik training models that uses TCP communication to interface with an OpenPLC Runtime server. It currently supports only an **Indexed Line with two machine stations**.

## Features

- **Indexed Line Simulation:** Only supports an Indexed Line with two machine stations.
- **TCP Communication:** Data exchange with an OpenPLC server using TCP.
- **OpenPLC Runtime PSM:** OpenPLC Runtime PSM driver.

## Getting Started

### Prerequisites

- **OpenPLC Runtime:** An operational OpenPLC server with TCP/IP connectivity.

### Configuration

- Edit the configuration file at **FTsim-OpenPLC_Data\StreamingAssets\config-L2N.json** to set your OpenPLC connection details and I/O addresses.

### OpenPLC Python SubModule (PSM)
Driver for OpenPLC Runtime. 

```
import socket
import json
import time
import select
import psm

HOST = '0.0.0.0'
PORT = 5000

# Update speed in seconds (adjust this value as needed)
update_interval = 0.01

def init():
    psm.set_var("IX1.0", True)
    psm.set_var("IX0.6", True)
    psm.set_var("IX0.7", True)
    psm.set_var("IX1.1", True)
    psm.set_var("IX1.2", True)
    psm.set_var("IX0.3", True)
    psm.set_var("IX0.5", True)

def handle_connection(conn):
    conn.setblocking(False)
    last_qx_update = time.time()
    
    while True:
        try:
            ready = select.select([conn], [], [], 0.1)
        except Exception as e:
            print("[Server] Select error:", e)
            break

        if ready[0]:
            try:
                data = conn.recv(1024)
                if data:
                    command = json.loads(data.decode('utf-8'))
                    print("[Server] Received command:", command)
                    if command.get("action") == "set_IX":
                        psm.set_var(command.get("address"), command.get("value"))
                else:
                    print("[Server] No data – client may have disconnected.")
                    break
            except Exception as e:
                print("[Server] Read error:", e)
                break

        if time.time() - last_qx_update >= 0.01:
            qx_updates = {}
            for index in range(10):
                part1 = index // 8
                part2 = index % 8
                address = f"QX{part1}.{part2}"
                qx_updates[address] = psm.get_var(address)
            json_update = json.dumps(qx_updates)
            try:
                conn.sendall(json_update.encode('utf-8'))
                current_time = time.strftime("%H:%M:%S", time.localtime())
                print(f"[Server] Sent QX update ({current_time}): {json_update}")
            except Exception as e:
                print("[Server] Send error:", e)
                break
            last_qx_update = time.time()

        time.sleep(0.01)
    
    conn.close()
    print("[Server] Connection closed.")

def main():
    psm.start()
    init()
    server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server_socket.bind((HOST, PORT))
    server_socket.listen(1)
    print(f"[Server] Listening on {HOST}:{PORT}")
    
    try:
        while True:
            print("[Server] Waiting for client connection...")
            try:
                conn, addr = server_socket.accept()
            except Exception as e:
                print("[Server] Accept error:", e)
                continue
            
            print(f"[Server] Client connected from: {addr}")
            handle_connection(conn)
            print("[Server] Client disconnected. Ready for new connection.")
    except KeyboardInterrupt:
        pass
    finally:
        server_socket.close()
        psm.stop()

if __name__ == "__main__":
    main()

```

## Credits

FTsim-OpenPLC build on the work of [FTsim-ADS](https://github.com/laspp/FTsim-ADS).

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
