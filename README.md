# BTCSharp

BTCSharp is a work-in-progress **C#/.NET port of Bitcoin Core v29**.  
The goal is to create a fully managed Bitcoin node implementation that is **compatible with Bitcoin Core** at the protocol, CLI, and file-system level — without the wallet, mining, or GUI subsystems.

## ✨ Project Goals

- **Protocol Compatibility**  
  Behave like Bitcoin Core (`bitcoind`, `bitcoin-cli`) so existing tooling, configs, and scripts continue to work.

- **Managed Codebase**  
  Entirely written in C#/.NET (with small native shims only where absolutely necessary during the transition).

- **Modular Migration**  
  Source files are being replaced one-by-one from the original C++ codebase. Each module passes through the original test suite before being considered complete.

- **Node Only**  
  This project excludes wallet, mining, and GUI functionality to reduce complexity.  
  Focus is on full node validation, P2P networking, mempool, block relay, and RPC interface.

## 🚧 Current Status

- ✅ Bitcoin Core v29 builds and runs with wallet/GUI/mining removed.  
- 🔄 Incremental porting from C++ → C#:  
  - Encoding libraries (e.g. Bech32) under migration.  
  - Interop via P/Invoke / NativeAOT used temporarily.  
- 🧪 Original Core unit tests (`test_bitcoin`) are used to validate replaced modules.  

This is **alpha-stage** software and **not safe for production use**.

## 🔧 Building

### Prerequisites
- .NET 8 SDK
- CMake ≥ 3.22
- GCC/Clang (Linux/macOS) or MSVC (Windows)
- Dependencies for Bitcoin Core (libevent, Boost, etc.) if building native stubs

### Build Instructions

Clone the repository:
```bash
git clone https://github.com/yourusername/btcsharp.git
cd btcsharp
```

Build the pruned native Core (used while porting):
```bash
cmake -S . -B build -DENABLE_WALLET=OFF -DBUILD_GUI=OFF
cmake --build build --target bitcoind bitcoin-cli -j4
```

Build the managed components:
```bash
dotnet build src/BTCSharp.sln
```

Run tests:
```bash
dotnet test
```

## 📡 Usage

Run the node:
```bash
./build/bin/bitcoind -datadir=/path/to/data -printtoconsole=1
```

Use the CLI:
```bash
./build/bin/bitcoin-cli getblockchaininfo
```

## 🛠️ Roadmap

- [ ] Replace low-level encoding modules (Bech32, Base58, checksums)
- [ ] Replace cryptography layer (Secp256k1 → managed equivalent or binding)
- [ ] Replace networking/mempool
- [ ] Replace consensus/validation
- [ ] Achieve drop-in compatibility with Core RPC/CLI
- [ ] Remove all native dependencies

## 🤝 Contributing

Contributions are welcome! Please open issues or PRs if you’d like to help.  
Focus areas: porting modules, improving test coverage, validating cross-platform builds.

## 📜 License

BTCSharp is licensed under the [MIT License](LICENSE), the same as Bitcoin Core.

---

> **Note:** BTCSharp is a research and development project.  
> It is *not* a drop-in replacement for production Bitcoin Core nodes yet.
