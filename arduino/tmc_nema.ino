#include <TMCStepper.h>         // TMCstepper - https://github.com/teemuatlut/TMCStepper
#include <SoftwareSerial.h>     // Software serial for the UART to TMC2209 - https://www.arduino.cc/en/Reference/softwareSerial
#include <Streaming.h>          // For serial debugging output - https://www.arduino.cc/reference/en/libraries/streaming/
#include <Ramp.h>               // for ramping https://github.com/siteswapjuggler/RAMP
#include <Wire.h>               //
#include <VL53L0X.h>            // for distance sensor https://github.com/pololu/vl53l0x-arduino
#include <debounce.h>            // for button debounce https://github.com/kimballa/button-debounce


#define EN_PIN           2      // Enable
//#define DIR_PIN          3      // Direction 
//#define STEP_PIN         4      // Step
//#define SW_SCK           5      // Software Slave Clock (SCK)
#define SW_TX            6      // SoftwareSerial receive pin
#define SW_RX            7      // SoftwareSerial transmit pin
#define GO_BTN_PIN 10            // go/pause button pin
#define REW_BTN_PIN 8           // rewind button pin 
#define ADJUST_BTN_PIN 9       // go to center button pin
#define LED_PIN_11 12
#define LED_PIN_12 11
#define LED_PIN_5 5
#define DRIVER_ADDRESS   0b00   // TMC2209 Driver address according to MS1 and MS2
#define R_SENSE 0.11f           // look at TMC Driver // резистор на плате
#define HIGH_ACCURACY           // higher accuracy at the cost of lower speed for vl53l0 // выше точность для датчика vl53l0

SoftwareSerial SoftSerial(SW_RX, SW_TX);                          // Be sure to connect RX and TX (through R = 1 kOhm) of arduino to 4 pin of TMC //
TMC2209Stepper TMCdriver(&SoftSerial, R_SENSE, DRIVER_ADDRESS);   // Create TMC driver // создаем ТМС драйвер
rampLong myRamp;                               // new int ramp object //новый объкт RAMP
VL53L0X sensor;

//constants, max safe rps = 1000000
const long fVel = 7620;        //driver rps = (desired rps / 0.715) * steps * microsteps ; desired rps = х mm : 10 mm (daimeters of segments design by ReinerVogel to shaft)  * 100 (gearbox) revs / 86164 s (sidereal day) 
const long rVel = -70000;      //rewind velocity 
long corrVel = 0;      // correction of targetVel
long pulseVel = 0; // changed in PulseGuiding
long targetVelOld = 0;

// bools, for EQ platform with motor placed south from segments we need shaft rotation CLOCKWISE
// sysState variable, 0 = pause, 1 = go forward, 2 = rewind, 3 = correcting, 4 = at home; 5 = pulseguiding
uint8_t sysState = 4;
uint8_t sysStateDEBUG = 0; // for debug
bool sensorAvalible;
bool dir = true;

int prevDistance = 0; //for debug
int pulseDuration;  //TODO use this
uint8_t pulseDirection; //1 = WEST, 2 = EAST
unsigned long timer = 0; 

bool driven = true; // 1 if connected to ASCOM driver server, 0 if standalone; for debug messages

//leds blink
void ledBlink(uint8_t blinkMode) {
  unsigned long currentMillis = millis();
  // все 3 светдиода мигают раз в секунду, обозначает паузу sysState 0 или положение atHome sysState 4
  if (blinkMode == 0 || blinkMode == 4 ){
    switch (currentMillis % 1000)
      {
        case 0:
          digitalWrite(LED_PIN_11, LOW);
          digitalWrite(LED_PIN_12, LOW);
          digitalWrite(LED_PIN_5, LOW);
          break;
        case 500:
          digitalWrite(LED_PIN_11, HIGH);
          digitalWrite(LED_PIN_12, HIGH);
          digitalWrite(LED_PIN_5, HIGH);
          break;
      }
    }
  else if (blinkMode == 1){
    // Светодиоды светятся поочередно с перерывами в 500мс (по кругу)
    switch (currentMillis % 2000)
    {
      case 0:
        digitalWrite(LED_PIN_11, HIGH);
        digitalWrite(LED_PIN_12, LOW);
        digitalWrite(LED_PIN_5, LOW);
        break;
      case 500:
        digitalWrite(LED_PIN_11, LOW);
        digitalWrite(LED_PIN_12, HIGH);
        digitalWrite(LED_PIN_5, LOW);
        break;
      case 1000:
        digitalWrite(LED_PIN_11, LOW);
        digitalWrite(LED_PIN_12, LOW);
        digitalWrite(LED_PIN_5, HIGH);
        break;
      case 1500:
        digitalWrite(LED_PIN_11, LOW);
        digitalWrite(LED_PIN_12, LOW);
        digitalWrite(LED_PIN_5, LOW);
        break;
    }

  } else if (blinkMode == 2) {
    // Светодиоды светятся поочередно с перерывами в 400мс, но в обратном порядке.
      switch (currentMillis % 1600) // cycle duration 1600 ms
      {
        case 0:
          digitalWrite(LED_PIN_11, LOW);
          digitalWrite(LED_PIN_12, LOW);
          digitalWrite(LED_PIN_5, HIGH);
          break;
        case 400:
          digitalWrite(LED_PIN_11, LOW);
          digitalWrite(LED_PIN_12, HIGH);
          digitalWrite(LED_PIN_5, LOW);
          break;
        case 800:
          digitalWrite(LED_PIN_11, HIGH);
          digitalWrite(LED_PIN_12, LOW);
          digitalWrite(LED_PIN_5, LOW);
          break;
        case 1200:
          digitalWrite(LED_PIN_11, LOW);
          digitalWrite(LED_PIN_12, LOW);
          digitalWrite(LED_PIN_5, LOW);
          break;
      }
  }

  //светодиоды светятся отображая коррекцию скорости
  // cycle duration = 10*corrVel
  // center always on, side LEDs blink at value of correction velociy , for negative LED11, for positive LED13
  else if (blinkMode == 3) {
    digitalWrite(LED_PIN_12, HIGH);                         
    if (corrVel == 0) 
    {
      digitalWrite(LED_PIN_11, LOW);
      digitalWrite(LED_PIN_12, HIGH); 
      digitalWrite(LED_PIN_5, LOW);
    }
    else if (corrVel > 0){
      if (currentMillis % 1000 == 0){
        digitalWrite(LED_PIN_11, LOW);
        digitalWrite(LED_PIN_12, HIGH);

        digitalWrite(LED_PIN_5, HIGH);  
      }
      else if (currentMillis % 1000 == 500){
        digitalWrite(LED_PIN_11, LOW);
        digitalWrite(LED_PIN_12, HIGH);
        digitalWrite(LED_PIN_5, LOW);
      }
    }
    else if (corrVel < 0){
       if (currentMillis % 1000 == 0){
        digitalWrite(LED_PIN_11, HIGH);

        digitalWrite(LED_PIN_12, HIGH);
        digitalWrite(LED_PIN_5, LOW);  
      }
      else if (currentMillis % 1000 == 500){
        digitalWrite(LED_PIN_11, LOW);
        digitalWrite(LED_PIN_12, HIGH);
        digitalWrite(LED_PIN_5, LOW);
      }
    }
  }  
}

//== buttons stuff ==

// setup all 3 buttons
static Button buttonGo(GO_BTN_PIN, buttonHandler);
static Button buttonRew(REW_BTN_PIN, buttonHandler);
static Button buttonAdjust(ADJUST_BTN_PIN, buttonHandler);
static Button longBtn(ADJUST_BTN_PIN + 100, longPressHandler);


// set corrVel to match East or West command, calculate elapsed duration after time when command was sent
void pulseGuide()
{
  int elapsed = millis() - timer; 
  if (elapsed <= pulseDuration)        // keep pulseVel to +/- 3000 if pulseDuration hasn't ran out
  {
    if (!driven)
    {
      Serial << "Pulse to: " << pulseDirection << " for: "<< (pulseDuration - elapsed) << endl;
    }
    if (pulseDirection == 1)
    {
      pulseVel = -3000;
    }

    if (pulseDirection == 2)
    {
      pulseVel = 3000;
    }
  }
  else  //
  {
    timer = 0;
    pulseVel = 0;
    pulseDuration = 0;
    sysState = 1; //continue tracking
    return;
  }
}

// manage system states via button presses
static void buttonHandler(uint8_t btnId, uint8_t btnState) {
  if (btnState == BTN_OPEN) {
    //btnState == BTN_PRESSED
    if (btnId == GO_BTN_PIN)
    {
      if (sysState == 1) {
        sysState = 0;            // Pause state
      }
      else if (sysState == 3){ 
        corrVel += 10;
      }
      else {
        sysState = 1;            // Go forward state
      }
    }
    if (btnId == REW_BTN_PIN)
    {
      if (sysState == 2) { 
        sysState = 0;              // sysetm Pause state (from rewind)
      }
      else if (sysState == 3){ 
        corrVel -= 10;
      }
      else {
        sysState = 2;            // system Rewind state
      }
    }
    if (btnId == ADJUST_BTN_PIN)
    {
      if (sysState == 3) {
        sysState = 1; // on second press, return to forward movement
      }
      else {
        sysState = 3; //set sysState to speed adjustment
      }
    }
  }
}

//handle long press of ADJUST button
static void longPressHandler(uint8_t btnId, uint8_t btnState) {
  if (btnState == BTN_PRESSED) {
    // Send a message after it has been held down a long time.
    if (btnId == ADJUST_BTN_PIN) {
      if (!driven)
      {
        Serial.println("Adjust Button pressed and held a long time, corrVel set to 0");
      }
      driven = true;
      corrVel = 0;
    }
    if (btnId == GO_BTN_PIN) {
      if (!driven)
      {
        Serial.println("Go Button pressed and held a long time, switch driven");
      }
      if (driven){ 
        driven = false;
      }
      if (!driven){ 
        driven = true;
      }
    }
  } else {
    // btnState == BTN_OPEN.
    // Do nothing on button release.
    return;
  }
}

static void pollButtons() {
  // update() will call buttonHandler() if ***_BTN_PIN transitions to a new state and stays there for multiple reads over 25+ ms.
  // update all 3 buttons:
  longBtn.update(digitalRead(ADJUST_BTN_PIN));
  longBtn.update(digitalRead(GO_BTN_PIN));
  buttonGo.update(digitalRead(GO_BTN_PIN));
  buttonRew.update(digitalRead(REW_BTN_PIN));
  buttonAdjust.update(digitalRead(ADJUST_BTN_PIN));
}

//== Setup ===============================================================================

void setup() {

// setup Ramp
  myRamp.setGrain(1);                         // set grain to 1 ms 
  myRamp.go(0);                               // start at 0
//setup connections
  Serial.begin(57600);               // initialize hardware serial for debugging
  SoftSerial.begin(57600);           // initialize software serial for UART motor control
  TMCdriver.beginSerial(57600);      // Initialize UART
 
  Serial.flush();

// Set pinmodes
  pinMode(EN_PIN, OUTPUT);           
//  pinMode(STEP_PIN, OUTPUT);
//  pinMode(DIR_PIN, OUTPUT);
  pinMode(GO_BTN_PIN, INPUT_PULLUP);
  pinMode(REW_BTN_PIN, INPUT_PULLUP);
  pinMode(ADJUST_BTN_PIN, INPUT_PULLUP);
  pinMode(LED_PIN_11, OUTPUT);
  pinMode(LED_PIN_12, OUTPUT);
  pinMode(LED_PIN_5, OUTPUT);

  // buttonGo, buttonRew stay with the default 25ms (`BTN_DEBOUNCE_MILLIS`) debounce time interval.
  // Must hold buttonAdjust down for 1s to trigger `longPressHandler()`; a short press won't cut it.
  // This means it will take a second to trigger buttonHandler(..., BTN_PRESSED).
  // It will then take only the default 25ms to trigger buttonHandler(..., BTN_OPEN).
  longBtn.setPushDebounceInterval(1000);  // 1000 ms = 1 second
  
  //setupTMC driver
  digitalWrite(EN_PIN, LOW);         // Enable TMC2209 board  

  TMCdriver.begin();                                                                                                                                                                                                                                                                                                                            // UART: Init SW UART (if selected) with default 115200 baudrate
  TMCdriver.toff(5);                 // Enables driver in software
  TMCdriver.rms_current(500);        // Set motor RMS current
  TMCdriver.microsteps(256);         // Set microsteps

  TMCdriver.en_spreadCycle(false);
  TMCdriver.pwm_autoscale(true);     // Needed for stealthChop
  TMCdriver.shaft(1);

// setup i2c sensor vl53l0x
  Wire.begin();
  sensor.setTimeout(500);
  if (!sensor.init())
  {
    if (!driven)
    {
      Serial.println("Failed to detect and initialize sensor!");  //blink 5 times to indicate sensor failrue
    }
    sensorAvalible = false;
    for (int i = 0; i <=5; i++) {
      digitalWrite(LED_PIN_11, HIGH);
      delay(500);
      digitalWrite(LED_PIN_11, LOW);
      delay(500);
    }
  }
  else
  {
    sensorAvalible = true;
    sensor.setMeasurementTimingBudget(150000);
  }
}

//== Loop =================================================================================

void loop() {
  long targetVel;
  long currentVel = TMCdriver.VACTUAL();
  // first read sensor
  if (millis() % 10100 == 0){
    if (!sensorAvalible)
    {
      if (!driven)
      {
        Serial << "sensor not avalible" << endl;
      }

    }
    int distance = sensor.readRangeSingleMillimeters();
    if (abs(distance - prevDistance) >= 2 && !driven ){
      Serial << "distance: "<< distance << endl;
      prevDistance = distance;
    }
    if (sensor.timeoutOccurred())
    {
      if (!driven)
    {
        Serial.print("SENSOR TIMEOUT");
    }
      sensorAvalible = false;
      //return;
    }
  // do distance checks
    if (distance <=35 && sysState == 1 && sensorAvalible)
    {
      sysState = 2;           //auto rewind at end
    }
    else if (distance >= 105 && sysState == 2 && sensorAvalible)   //auto stop athome when rewinding, set atHome, set driven = true
    {
      sysState = 4;
      driven = true;
    }
  }
  //read buttons
  pollButtons();
  if (sysState == 0 || sysState == 4) // GO is pressed other time, others were not pressed || atHome
  {
    targetVel = 0;
  }
  else if (sysState == 1 || sysState == 3 || sysState == 5) // GO is pressed once, currently correcting tracking, also if pulseGuide was sent
  {
    targetVel = fVel + corrVel + pulseVel; //apply velocity corrections
    if (sysState == 5) 
    {
      pulseGuide();
    }
  }
  else if (sysState == 2) // REW was pressed
  {
    targetVel = rVel;
  }

  //change velocity fo motor if needed
  if (targetVel != targetVelOld) // if targetVel was changed
  {
    int rampTime; // set adequate ramp time in ms
    if (sysState == 5)
    {
      rampTime = 40;      // for pulseGuide 
    }
    else
    {
      rampTime = (max(abs(targetVel), abs(currentVel)) - min(abs(targetVel), abs(currentVel)))/4;
    }
    if (!driven)
    {
      Serial << "targetVel changed: " << targetVel << " rampTime: "<< rampTime << endl;
    }
    myRamp.go(targetVel, rampTime, QUADRATIC_IN, ONCEFORWARD);       //go to new speed 
  }
//    rampVelocity(vel, TMCdriver.VACTUAL());
  if (currentVel != targetVel)       // async ramp update at given intervals AND if update is needed
  {
    currentVel = myRamp.getValue();                        // store current value of ramp into variable
    myRamp.update();                                      // update ramp                 
    TMCdriver.VACTUAL(currentVel);                              // send velocity to TMC2209 via UART
    if (!driven)
    {
      Serial << "ramp updates TMC, current velocity set to: "<< currentVel << endl;                // read current value to serial output
    }
  }
  targetVelOld = targetVel;

    if (!driven && sysStateDEBUG != sysState)
  {
    Serial << "sysState set to: "<< sysState << endl;
  }
  sysStateDEBUG = sysState;
  //blink with LEDs according to sysState
  ledBlink(sysState);
}

 /* SerialEvent occurs whenever a new data comes in the hardware serial RX. The pulses are put out in the form of <direction#duration[in ms]#>. ( ex: "E#400#" )
 This routine is run between each time loop() runs, so using delay inside
 the loop() can delay response.  Multiple bytes of data may be available. */

void serialEvent() {
  if(Serial.available()>0) 
  {
      String driver_cmd;
      driver_cmd = Serial.readStringUntil('#');
  
      if(driver_cmd == "CT")   // GET Tracking
      {
        if(sysState == 1 || sysState == 3)
        {
          Serial.print("TRUE#");
        }
        else
        {
          Serial.print("FALSE#");
        }  
      }

      else if(driver_cmd == "I")   // Get IsPulseguiding
      {
        if(sysState == 3)
        {
          Serial.print("TRUE#");
        }
        else
        {
          Serial.print("FALSE#");
        }  
      }

      else if(driver_cmd == "CS")   // Get Slewing
      {
        if(sysState == 2 )
        {
          Serial.print("TRUE#");
        }
        else
        {
          Serial.print("FALSE#");
        }  
      }

      else if(driver_cmd == "ATHOME")   // GET athome
      {
        if(sysState == 4)
        {
          Serial.print("TRUE#");
        }
        else
        {
          Serial.print("FALSE#");
        }  
      }
      
      /*
      // move motor 1 step north
      
      if(driver_cmd == "N") {
        driver_cmd = "";
        driver_cmd = Serial.readStringUntil('#');
        dur = driver_cmd.toInt();             
        Move(0, dur);          
        isPulseGuiding = true;
      }
      
      // move motor 1 step south 
    
      if(driver_cmd == "S") {
        driver_cmd = "";
        driver_cmd = Serial.readStringUntil('#');
        dur = driver_cmd.toInt();                
        Move(1, dur);                 
        isPulseGuiding = true;
      }
      */

      // set pulse west
      
      else if(driver_cmd == "W") {
        driver_cmd = "";
        driver_cmd = Serial.readStringUntil('#');
        pulseDuration = driver_cmd.toInt(); 
        pulseDirection = 1;
        sysState = 5;
        timer = millis();
        Serial.print("TRUE#");
      }

      // set pulse east
      
      else if(driver_cmd == "E") {
        driver_cmd = "";
        driver_cmd = Serial.readStringUntil('#');
        pulseDuration = driver_cmd.toInt();
        pulseDirection = 2;                     
        sysState = 5;
        timer = millis();
        Serial.print("TRUE#");
      }

      // rewind
      else if(driver_cmd == "HOME") {
        driver_cmd = "";
        sysState = 2;
        Serial.print("TRUE#");
      }

      // stop
      else if(driver_cmd == "A") {
        driver_cmd = "";
        sysState = 0;
        Serial.print("TRUE#");
      }

      // track
      else if(driver_cmd == "T") {
        driver_cmd = "";
        sysState = 1;
        Serial.print("TRUE#");
      }

      // pause
      else if(driver_cmd == "TS" || driver_cmd == "H") {
        driver_cmd = "";
        sysState = 0;
        Serial.print("FALSE#");
      }

      else
      {
        
      }  

    }
}
