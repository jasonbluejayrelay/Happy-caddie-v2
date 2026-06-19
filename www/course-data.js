// North Hampton Golf Club — Fernandina Beach, FL
// GPS coordinates are approximate; walk to each green and use "Update Pin" to refine.
// Yardages are championship (black) / gold / blue / white / red

const COURSES = [
  {
    id: 'north-hampton',
    name: 'North Hampton Golf Club',
    address: '940287 Golf Club Dr, Fernandina Beach, FL 32034',
    phone: '(904) 491-4104',
    par: 72,
    tees: [
      { id: 'black',  name: 'Black',  color: '#111111', textColor: '#fff', rating: 73.4, slope: 135 },
      { id: 'gold',   name: 'Gold',   color: '#DAA520', textColor: '#111', rating: 71.2, slope: 131 },
      { id: 'blue',   name: 'Blue',   color: '#1565C0', textColor: '#fff', rating: 69.8, slope: 127 },
      { id: 'white',  name: 'White',  color: '#EEEEEE', textColor: '#111', rating: 67.5, slope: 121 },
      { id: 'red',    name: 'Red',    color: '#C62828', textColor: '#fff', rating: 70.1, slope: 122 }
    ],
    holes: [
      {
        number: 1, par: 4, handicap: 11,
        yardages: { black: 380, gold: 365, blue: 348, white: 320, red: 285 },
        tee:  { lat: 30.6742, lon: -81.5533 },
        pin:  { lat: 30.6766, lon: -81.5508 },
        description: 'Slight dogleg right. Bunker right at 220 yds. Open fairway.',
        carries: [{ label: 'Clear RH bunker', fromTee: 220 }]
      },
      {
        number: 2, par: 5, handicap: 1,
        yardages: { black: 520, gold: 505, blue: 488, white: 462, red: 425 },
        tee:  { lat: 30.6766, lon: -81.5508 },
        pin:  { lat: 30.6810, lon: -81.5491 },
        description: 'Long par 5. Water left on approach. Reachable in two for big hitters.'
      },
      {
        number: 3, par: 3, handicap: 17,
        yardages: { black: 185, gold: 172, blue: 158, white: 143, red: 125 },
        tee:  { lat: 30.6810, lon: -81.5491 },
        pin:  { lat: 30.6810, lon: -81.5473 },
        description: 'All carry over water. Club up — green slopes back to front.'
      },
      {
        number: 4, par: 4, handicap: 5,
        yardages: { black: 390, gold: 378, blue: 361, white: 340, red: 308 },
        tee:  { lat: 30.6810, lon: -81.5473 },
        pin:  { lat: 30.6788, lon: -81.5450 },
        description: 'Dogleg left. Drive must carry bunkers at 240. Narrow approach.',
        carries: [{ label: 'Carry fairway bunkers', fromTee: 240 }]
      },
      {
        number: 5, par: 4, handicap: 9,
        yardages: { black: 400, gold: 385, blue: 368, white: 345, red: 310 },
        tee:  { lat: 30.6788, lon: -81.5450 },
        pin:  { lat: 30.6756, lon: -81.5450 },
        description: 'Straight par 4. Tree-lined fairway. Bunkered green.'
      },
      {
        number: 6, par: 3, handicap: 15,
        yardages: { black: 175, gold: 160, blue: 147, white: 132, red: 115 },
        tee:  { lat: 30.6756, lon: -81.5450 },
        pin:  { lat: 30.6756, lon: -81.5467 },
        description: 'Island-style green surrounded by sand. Wind is a major factor.'
      },
      {
        number: 7, par: 5, handicap: 3,
        yardages: { black: 510, gold: 495, blue: 478, white: 452, red: 415 },
        tee:  { lat: 30.6756, lon: -81.5467 },
        pin:  { lat: 30.6724, lon: -81.5497 },
        description: 'Dogleg left par 5. Water guards left side all the way. Risk/reward second shot.'
      },
      {
        number: 8, par: 4, handicap: 13,
        yardages: { black: 365, gold: 350, blue: 334, white: 312, red: 278 },
        tee:  { lat: 30.6724, lon: -81.5497 },
        pin:  { lat: 30.6744, lon: -81.5516 },
        description: 'Short par 4. Driveable for long hitters. Tight landing area.'
      },
      {
        number: 9, par: 4, handicap: 7,
        yardages: { black: 395, gold: 380, blue: 362, white: 338, red: 302 },
        tee:  { lat: 30.6744, lon: -81.5516 },
        pin:  { lat: 30.6777, lon: -81.5520 },
        description: 'Uphill finishing hole. Bunkers left and right of green. Strong finish.'
      },
      {
        number: 10, par: 4, handicap: 8,
        yardages: { black: 385, gold: 370, blue: 352, white: 328, red: 294 },
        tee:  { lat: 30.6777, lon: -81.5520 },
        pin:  { lat: 30.6777, lon: -81.5557 },
        description: 'Slight dogleg left. Fairway bunker at 250 on the left.',
        carries: [{ label: 'Clear LH bunker', fromTee: 250 }]
      },
      {
        number: 11, par: 3, handicap: 14,
        yardages: { black: 195, gold: 180, blue: 165, white: 148, red: 130 },
        tee:  { lat: 30.6777, lon: -81.5557 },
        pin:  { lat: 30.6762, lon: -81.5568 },
        description: 'Longest par 3. Two-tiered green. Miss short for best up-and-down.'
      },
      {
        number: 12, par: 5, handicap: 2,
        yardages: { black: 530, gold: 515, blue: 498, white: 470, red: 432 },
        tee:  { lat: 30.6762, lon: -81.5568 },
        pin:  { lat: 30.6718, lon: -81.5568 },
        description: 'Signature hole. Water right entire length. Best par 5 on the course.'
      },
      {
        number: 13, par: 4, handicap: 12,
        yardages: { black: 375, gold: 360, blue: 343, white: 320, red: 285 },
        tee:  { lat: 30.6718, lon: -81.5568 },
        pin:  { lat: 30.6718, lon: -81.5604 },
        description: 'Flat par 4. Deceptively long into prevailing wind. Avoid back bunkers.'
      },
      {
        number: 14, par: 4, handicap: 6,
        yardages: { black: 405, gold: 390, blue: 372, white: 348, red: 312 },
        tee:  { lat: 30.6718, lon: -81.5604 },
        pin:  { lat: 30.6741, lon: -81.5628 },
        description: 'Dogleg right. Placement drive required. Water behind green.'
      },
      {
        number: 15, par: 3, handicap: 18,
        yardages: { black: 170, gold: 155, blue: 142, white: 127, red: 110 },
        tee:  { lat: 30.6741, lon: -81.5628 },
        pin:  { lat: 30.6755, lon: -81.5628 },
        description: 'Downhill par 3. Take one less club. Pin positions are tricky.'
      },
      {
        number: 16, par: 5, handicap: 4,
        yardages: { black: 515, gold: 500, blue: 482, white: 456, red: 418 },
        tee:  { lat: 30.6755, lon: -81.5628 },
        pin:  { lat: 30.6755, lon: -81.5580 },
        description: 'Straightaway par 5. Go for it in two if you carry the fairway bunkers.',
        carries: [{ label: 'Carry fairway bunkers', fromTee: 250 }]
      },
      {
        number: 17, par: 4, handicap: 16,
        yardages: { black: 380, gold: 365, blue: 348, white: 325, red: 290 },
        tee:  { lat: 30.6755, lon: -81.5580 },
        pin:  { lat: 30.6778, lon: -81.5556 },
        description: 'Short par 4. Aggressive line over trees shortens hole significantly.',
        carries: [{ label: 'Carry trees (aggressive)', fromTee: 230 }]
      },
      {
        number: 18, par: 4, handicap: 10,
        yardages: { black: 415, gold: 400, blue: 382, white: 358, red: 320 },
        tee:  { lat: 30.6778, lon: -81.5556 },
        pin:  { lat: 30.6778, lon: -81.5518 },
        description: 'Finishing hole. Uphill to elevated green. Make par here and you earned it.'
      }
    ]
  }
];

const DEFAULT_CLUBS = [
  { id: 'driver',  name: 'Driver',  icon: '🏌' },
  { id: '3w',      name: '3 Wood',  icon: '🌲' },
  { id: '5w',      name: '5 Wood',  icon: '🌲' },
  { id: '4h',      name: '4 Hybrid',icon: '⬡' },
  { id: '4i',      name: '4 Iron',  icon: '⊥' },
  { id: '5i',      name: '5 Iron',  icon: '⊥' },
  { id: '6i',      name: '6 Iron',  icon: '⊥' },
  { id: '7i',      name: '7 Iron',  icon: '⊥' },
  { id: '8i',      name: '8 Iron',  icon: '⊥' },
  { id: '9i',      name: '9 Iron',  icon: '⊥' },
  { id: 'pw',      name: 'PW',      icon: '∧' },
  { id: 'gw',      name: 'Gap (52)',icon: '∧' },
  { id: 'sw',      name: 'Sand (56)',icon: '∧' },
  { id: 'lw',      name: 'Lob (60)',icon: '∧' },
  { id: 'putter',  name: 'Putter',  icon: '|' }
];
