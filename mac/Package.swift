// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "SideTodo",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "SideTodo", targets: ["SideTodo"])],
    targets: [.executableTarget(name: "SideTodo")]
)
