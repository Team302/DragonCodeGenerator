// Copyright (c) FIRST and other WPILib contributors.
// Open Source Software; you can modify and/or share it under the terms of
// the WPILib BSD license file in the root directory of this project.

#include "Robot.h"

#include <frc2/command/CommandScheduler.h>

#include "configs/MechanismConfig.h"
#include "configs/MechanismConfigMgr.h"
#include "ctre/phoenix6/SignalLogger.hpp"
#include "frc/RobotController.h"
#include "frc/Threads.h"
#include "RobotIdentifier.h"
#include "state/RobotState.h"
#include "teleopcontrol/TeleopControl.h"
#include "utils/DragonField.h"
#include "utils/logging/debug/Logger.h"
#include "utils/logging/signals/DragonDataLoggerMgr.h"
#include "utils/PeriodicLooper.h"
#include "utils/RoboRio.h"
#include "utils/sensors/SensorData.h"
#include "utils/sensors/SensorDataMgr.h"

Robot::Robot()
{
    Logger::GetLogger()->PutLoggingSelectionsOnDashboard();

    InitializeRobot();
    InitializeDriveteamFeedback();
    m_datalogger = DragonDataLoggerMgr::GetInstance();
}

void Robot::RobotPeriodic()
{
    frc2::CommandScheduler::GetInstance().Run();

    isFMSAttached = frc::DriverStation::IsFMSAttached();
    if (!isFMSAttached)
    {
        Logger::GetLogger()->PeriodicLog();
    }

    if (m_robotState != nullptr)
    {
        m_robotState->Run();
    }
}

void Robot::DisabledPeriodic()
{
}

void Robot::AutonomousInit()
{
    frc::SetCurrentThreadPriority(true, 15);

    PeriodicLooper::GetInstance()->AutonRunCurrentState();
}

void Robot::AutonomousPeriodic()
{
    SensorDataMgr::GetInstance()->CacheData();
    PeriodicLooper::GetInstance()->AutonRunCurrentState();
}

void Robot::TeleopInit()
{
    PeriodicLooper::GetInstance()->TeleopRunCurrentState();
    frc2::CommandScheduler::GetInstance().CancelAll();
}

void Robot::TeleopPeriodic()
{
    SensorDataMgr::GetInstance()->CacheData();
    PeriodicLooper::GetInstance()->TeleopRunCurrentState();
}

void Robot::TestInit()
{
    frc2::CommandScheduler::GetInstance().CancelAll();
}

void Robot::InitializeRobot()
{
    int32_t teamNumber = frc::RobotController::GetTeamNumber();
    RoboRio::GetInstance();

    MechanismConfigMgr::GetInstance()->InitRobot((RobotIdentifier)teamNumber);

    m_robotState = RobotState::GetInstance();
    m_robotState->Init();
}

void Robot::InitializeDriveteamFeedback()
{
    m_field = DragonField::GetInstance(); // TODO: move to drive team feedback
}

#ifndef RUNNING_FRC_TESTS
int main()
{
    return frc::StartRobot<Robot>();
}
#endif
