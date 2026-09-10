package adserver

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"sync"
)

type dataStore struct {
	mu      sync.RWMutex
	dataDir string
	ad      Advertisement
	stats   StatsDocument
}

func openStore(dataDir string) (*dataStore, error) {
	if err := os.MkdirAll(filepath.Join(dataDir, "media"), 0o750); err != nil {
		return nil, err
	}
	store := &dataStore{
		dataDir: dataDir,
		ad:      Advertisement{Version: 1, Enabled: false},
		stats:   StatsDocument{Campaigns: map[string]CampaignStats{}},
	}
	if err := readJSON(filepath.Join(dataDir, "config.json"), &store.ad); err != nil && !errors.Is(err, os.ErrNotExist) {
		return nil, err
	}
	if err := readJSON(filepath.Join(dataDir, "stats.json"), &store.stats); err != nil && !errors.Is(err, os.ErrNotExist) {
		return nil, err
	}
	if store.stats.Campaigns == nil {
		store.stats.Campaigns = map[string]CampaignStats{}
	}
	return store, nil
}

func (store *dataStore) getAd() Advertisement {
	store.mu.RLock()
	defer store.mu.RUnlock()
	return store.ad
}

func (store *dataStore) setAd(ad Advertisement) error {
	store.mu.Lock()
	defer store.mu.Unlock()
	if err := writeJSONAtomic(filepath.Join(store.dataDir, "config.json"), ad); err != nil {
		return err
	}
	store.ad = ad
	return nil
}

func (store *dataStore) record(campaignID, event string) error {
	store.mu.Lock()
	defer store.mu.Unlock()
	stats := store.stats.Campaigns[campaignID]
	if event == "impression" {
		stats.Impressions++
	} else {
		stats.Clicks++
	}
	store.stats.Campaigns[campaignID] = stats
	return writeJSONAtomic(filepath.Join(store.dataDir, "stats.json"), store.stats)
}

func (store *dataStore) getStats() StatsDocument {
	store.mu.RLock()
	defer store.mu.RUnlock()
	copy := StatsDocument{Campaigns: make(map[string]CampaignStats, len(store.stats.Campaigns))}
	for key, value := range store.stats.Campaigns {
		copy.Campaigns[key] = value
	}
	return copy
}

func readJSON(path string, target any) error {
	file, err := os.Open(path)
	if err != nil {
		return err
	}
	defer file.Close()
	return json.NewDecoder(file).Decode(target)
}

func writeJSONAtomic(path string, value any) error {
	temporary, err := os.CreateTemp(filepath.Dir(path), ".codex-ad-*.tmp")
	if err != nil {
		return err
	}
	temporaryName := temporary.Name()
	defer os.Remove(temporaryName)
	encoder := json.NewEncoder(temporary)
	encoder.SetEscapeHTML(false)
	if err = encoder.Encode(value); err == nil {
		err = temporary.Sync()
	}
	if closeErr := temporary.Close(); err == nil {
		err = closeErr
	}
	if err != nil {
		return err
	}
	if err = os.Chmod(temporaryName, 0o640); err != nil {
		return err
	}
	return os.Rename(temporaryName, path)
}
