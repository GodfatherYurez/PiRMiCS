// Copyright Epic Games, Inc. All Rights Reserved.

#include "NetworkLabGameMode.h"
#include "NetworkLabReplicationActor.h"
#include "Engine/World.h"
#include "Engine/Engine.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "GameFramework/GameStateBase.h"
#include "Engine/NetConnection.h"
#include "Engine/NetDriver.h"

namespace
{
	uint64 GetTotalBytesSentToClients(const UNetDriver* NetDriver)
	{
		if (!NetDriver)
		{
			return 0;
		}

		uint64 Total = 0;
		for (const TObjectPtr<UNetConnection>& Connection : NetDriver->ClientConnections)
		{
			if (IsValid(Connection))
			{
				Total += static_cast<uint64>(FMath::Max(Connection->OutTotalBytes, 0));
			}
		}
		return Total;
	}
}

ANetworkLabGameMode::ANetworkLabGameMode()
{
	PrimaryActorTick.bCanEverTick = true;
}

void ANetworkLabGameMode::BeginPlay()
{
	Super::BeginPlay();
	if (!HasAuthority())
	{
		return;
	}

	constexpr int32 MarkerCount = 50;
	constexpr float Radius = 1450.0f;
	bReplicationOptimizationEnabled = FParse::Param(FCommandLine::Get(), TEXT("NetworkLabOptimized"));
	for (int32 Index = 0; Index < MarkerCount; ++Index)
	{
		const float Angle = 2.0f * PI * static_cast<float>(Index) / static_cast<float>(MarkerCount);
		const FVector Location(FMath::Cos(Angle) * Radius, FMath::Sin(Angle) * Radius, 45.0f);
		const FTransform SpawnTransform(FRotator::ZeroRotator, Location);
		ANetworkLabReplicationActor* Marker = GetWorld()->SpawnActorDeferred<ANetworkLabReplicationActor>(ANetworkLabReplicationActor::StaticClass(), SpawnTransform);
		if (Marker)
		{
			Marker->Configure(Index % 5 == 0, Index + 1);
			Marker->FinishSpawning(SpawnTransform);
			if (bReplicationOptimizationEnabled)
			{
				Marker->SetOptimizationEnabled(true);
			}
			ReplicationMarkers.Add(Marker);
		}
	}

	UE_LOG(LogTemp, Display, TEXT("[ReplicationLab] Spawned %d replicated markers. Initial profile: %s. Press F2 in the listen-server window to toggle."), ReplicationMarkers.Num(), bReplicationOptimizationEnabled ? TEXT("OPTIMIZED") : TEXT("BASELINE"));
	if (FParse::Param(FCommandLine::Get(), TEXT("NetworkLabCapture")))
	{
		UE_LOG(LogTemp, Display, TEXT("[ReplicationLab] Automatic network profile capture will start when two players are connected."));
	}
}

void ANetworkLabGameMode::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (bCaptureFinished || !FParse::Param(FCommandLine::Get(), TEXT("NetworkLabCapture")))
	{
		return;
	}

	if (!bCaptureStarted)
	{
		if (!GetGameState<AGameStateBase>() || GetGameState<AGameStateBase>()->PlayerArray.Num() < 2)
		{
			return;
		}
		CaptureElapsedSeconds = 0.0f;
		bCaptureStarted = true;
		UE_LOG(LogTemp, Display, TEXT("[ReplicationLab] Two players connected. Network Profiler warms up for 5s before the 20s sample."));
	}

	CaptureElapsedSeconds += DeltaSeconds;
	if (CaptureElapsedSeconds >= 5.0f && CaptureElapsedSeconds < 5.0f + DeltaSeconds)
	{
		GEngine->Exec(GetWorld(), TEXT("netprofile enable"));
		OutgoingClientBytesAtCaptureStart = GetTotalBytesSentToClients(GetWorld()->GetNetDriver());
		UE_LOG(LogTemp, Display, TEXT("[ReplicationLab] Network Profiler recording started; outgoing network counter baseline=%llu bytes."), static_cast<unsigned long long>(OutgoingClientBytesAtCaptureStart));
	}
	else if (CaptureElapsedSeconds >= 25.0f)
	{
		GEngine->Exec(GetWorld(), TEXT("netprofile disable"));
		bCaptureFinished = true;
		const uint64 OutgoingBytes = GetTotalBytesSentToClients(GetWorld()->GetNetDriver()) - OutgoingClientBytesAtCaptureStart;
		const double CaptureSeconds = FMath::Max(CaptureElapsedSeconds - 5.0f, 1.0f);
		const double KilobytesPerSecond = static_cast<double>(OutgoingBytes) / 1024.0 / CaptureSeconds;
		UE_LOG(LogTemp, Display, TEXT("[ReplicationLab] NET_METRIC mode=%s actors=%d outgoing=%llu bytes duration=%.2f s average=%.2f KB/s"), bReplicationOptimizationEnabled ? TEXT("optimized") : TEXT("baseline"), ReplicationMarkers.Num(), static_cast<unsigned long long>(OutgoingBytes), CaptureSeconds, KilobytesPerSecond);
		UE_LOG(LogTemp, Display, TEXT("[ReplicationLab] Network Profiler recording stopped. Inspect Saved/Profiling for the .nprof file."));
	}
}

void ANetworkLabGameMode::ToggleReplicationOptimization()
{
	if (!HasAuthority())
	{
		return;
	}

	bReplicationOptimizationEnabled = !bReplicationOptimizationEnabled;
	for (ANetworkLabReplicationActor* Marker : ReplicationMarkers)
	{
		if (IsValid(Marker))
		{
			Marker->SetOptimizationEnabled(bReplicationOptimizationEnabled);
		}
	}

	const TCHAR* ModeText = bReplicationOptimizationEnabled ? TEXT("OPTIMIZED") : TEXT("BASELINE");
	UE_LOG(LogTemp, Display, TEXT("[ReplicationLab] Mode: %s. Replicated markers: %d."), ModeText, ReplicationMarkers.Num());
	if (GEngine)
	{
		GEngine->AddOnScreenDebugMessage(61284, 4.0f, bReplicationOptimizationEnabled ? FColor::Green : FColor::Orange,
			FString::Printf(TEXT("Replication profile: %s (50 markers)"), ModeText));
	}
}
