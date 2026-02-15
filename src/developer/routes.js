const express = require('express');
const router = express.Router();

// Endpoint to get current satsPerCall
router.get('/config/satsPerCall', (req, res) => {
  // Logic to retrieve satsPerCall
});

// Endpoint to get maxSatsPerDay
router.get('/config/maxSatsPerDay', (req, res) => {
  // Logic to retrieve maxSatsPerDay
});

module.exports = router;
